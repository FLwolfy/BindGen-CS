using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Managed;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Validation;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeFunctionAliasTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task NativeAndConfiguredAliasesShareOneRealImportAndTheOriginalLifetimePlan(BindingImportMode mode, bool sourceRuntime)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-native-function-alias-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "alias.h"), """
                #if defined(_WIN32)
                #define FIXTURE_EXPORT __declspec(dllexport)
                #else
                #define FIXTURE_EXPORT __attribute__((visibility("default")))
                #endif
                /** Adds one. @param value The original value. @return The incremented value. */
                FIXTURE_EXPORT int add(int value);
                #define NATIVE_ALIAS (add)
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "alias.c"), "#include \"alias.h\"\nFIXTURE_EXPORT int add(int value) { return value + 1; }\n");
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "aliases", nativeName = "alias_fixture" },
                source = new() { language = SourceLanguage.C, headers = ["alias.h"], includeTransitivelyReferencedHeaders = false },
                api = new()
                {
                    @namespace = "Generated.Aliases", name = "AliasApi", importMode = mode,
                    style = sourceRuntime ? ManagedApiStyle.Static : ManagedApiStyle.Instance,
                    names = ImmutableDictionary<string, string>.Empty.Add("NATIVE_ALIAS", "NativeAlias"),
                    functionAliases = [new("NATIVE_ALIAS", "ConfiguredAlias")],
                    functionContainers = [new("Math", [new("NATIVE_ALIAS", "Increment"), new("add", "AddOne")])]
                },
                runtime = new() { mode = sourceRuntime ? "source" : "package" },
                output = new() { layout = sourceRuntime ? "singleFile" : "split" },

                cache = new() { enabled = false }
            };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            GenerationResult result = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, root));
            Assert.True(result.success, result.GetDiagnosticSummary());
            TargetInspection target = Assert.Single(result.targetModels);
            Assert.Equal("add", Assert.Single(target.interop.functions).exportName);
            Assert.Equal(target.interop.functions[0].id, Assert.Single(target.interop.functionAliases).nativeFunction);
            Assert.Equal(2, target.managed!.aliases.Length);
            string targetRoot = Path.Combine(result.outputRoot, target.targetId);
            string binary = Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(targetRoot)));
            CSharpEmissionPlan plan = new CSharpEmissionPlanner().Plan(target.managed, new(configuration.library.nativeName, new NoWrites())
            { importMode = mode, runtimeNamespace = sourceRuntime ? "Generated.Aliases.Runtime" : "BGCS.Runtime", includeRuntimeSource = sourceRuntime });
            Assert.Single(plan.methods);
            Assert.Equal(2, plan.aliases.Length);
            Assert.Single(plan.functionContainers);
            var sources = Directory.GetFiles(Path.Combine(targetRoot, "managed"), "*.cs", SearchOption.AllDirectories)
                .Select(path => (Path: path, Text: File.ReadAllText(path))).ToArray();
            new EmittedInteropContractReader().Verify(plan, sources, configuration.library.nativeName);
            Assert.Contains(sources, source => source.Text.Contains("this.Add(", StringComparison.Ordinal));
            Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan,
                sources.Select(source => (source.Path, source.Text.Replace("this.Add(", "this.ConfiguredAlias(", StringComparison.Ordinal))), configuration.library.nativeName));
            string concrete = sourceRuntime ? "AliasApiContext" : "AliasApi";
            string factory = mode == BindingImportMode.FunctionTable ? "ApiContext.Load(binary)" : "ApiContext.Create()";
            string initializeShared = mode == BindingImportMode.FunctionTable ? "AliasApi.Load(binary);" : "AliasApi.Create();";
            string shared = sourceRuntime ? initializeShared + "\nif (AliasApi.NativeAlias(41) != 42 || AliasApi.ConfiguredAlias(41) != 42 || AliasApi.Math.AddOne(41) != 42) return 4;\nAliasApi.Dispose();" : "";
            string consumer = $$"""
                using System;
                using Generated.Aliases;
                using ApiContext = Generated.Aliases.{{concrete}};
                public static class AliasConsumer {
                    public static int Run(string binary) {
                        using var api = {{factory}};
                        if (api.Raw.Add(41) != 42 || api.Raw.NativeAlias(41) != 42 || api.Raw.ConfiguredAlias(41) != 42) return 1;
                        if (api.Add(41) != 42 || api.NativeAlias(41) != 42 || api.ConfiguredAlias(41) != 42) return 2;
                        var view = api.Math;
                        if (view.Increment(41) != 42 || view.AddOne(41) != 42) return 5;
                        {{shared}}
                        api.Dispose();
                        try { _ = api.Raw.ConfiguredAlias(41); return 3; } catch (ObjectDisposedException) { }
                        try { _ = view.Increment(41); return 6; } catch (ObjectDisposedException) { }
                        return 42;
                    }
                }
                """;
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot, "managed"), binary,
                configuration.library.nativeName, consumer, "AliasConsumer", root, sourceRuntime));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class NoWrites : IArtifactSink
    {
        public string WriteText(string relativePath, string content) => throw new InvalidOperationException("Planning must not emit sources.");
    }

    private static string WorkerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(directory!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
