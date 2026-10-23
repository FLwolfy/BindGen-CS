using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core;
using BGCS.Core.Inputs;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeCHeaderDefaultPolicyTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable)]
    [InlineData(BindingImportMode.DllImport)]
    [InlineData(BindingImportMode.LibraryImport)]
    public async Task GeneratedBindingsLoadAnExternallyCompiledOriginalModuleWithoutHeaderHelperExports(BindingImportMode mode)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-prebuilt-c-header-default-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "library.h"), """
                #pragma once
                #if defined(_WIN32)
                #define FIXTURE_EXPORT __declspec(dllexport)
                #else
                #define FIXTURE_EXPORT __attribute__((visibility("default")))
                #endif
                FIXTURE_EXPORT int External(int value);
                static int Local(int value) { return value + 2; }
                static inline int HeaderOnly(int value) { return value + 3; }
                inline int InlineOnly(int value) { return value + 4; }
                #define HELPER_ALIAS (HeaderOnly)
                #define EXTERNAL_ALIAS (External)
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "library.c"), "#include \"library.h\"\nFIXTURE_EXPORT int External(int value) { return value + 1; }\n");
            BindingConfiguration configuration = Configuration() with { api = Configuration().api with { importMode = mode } };
            Assert.False(configuration.source.includeHeaderHelpers);
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            GenerationResult built = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, root));
            Assert.True(built.success, built.GetDiagnosticSummary());
            string targetId = Assert.Single(built.targetModels).targetId;
            string builtBinary = Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(Path.Combine(built.outputRoot, targetId))));
            string binary = Path.Combine(root, Path.GetFileName(builtBinary));
            File.Copy(builtBinary, binary);
            {
                GenerationResult generated = await host.GenerateAsync(new(configuration, root));
                Assert.True(generated.success, generated.GetDiagnosticSummary());
                TargetInspection target = Assert.Single(generated.targetModels);
                Assert.Equal("External", Assert.Single(target.interop.functions).exportName);
                Assert.Equal("EXTERNAL_ALIAS", Assert.Single(target.interop.functionAliases).name);
                Assert.DoesNotContain(target.interop.functions, function => function.sourceName is "Local" or "HeaderOnly" or "InlineOnly");
                Assert.DoesNotContain(target.interop.functionAliases, alias => alias.name == "HELPER_ALIAS");
                Assert.DoesNotContain(generated.artifacts, artifact => artifact.relativePath.Contains("_helpers.", StringComparison.Ordinal)
                    || artifact.relativePath.EndsWith("header-helpers.manifest.json", StringComparison.Ordinal));
                string factory = mode == BindingImportMode.FunctionTable ? "OriginalApi.Load(binary)" : "OriginalApi.Create()";
                Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(generated.outputRoot, targetId, "managed"), binary,
                    configuration.library.nativeName, $$"""
                    using Generated.Original;
                    public static class OriginalConsumer {
                        public static int Run(string binary) {
                            using var api = {{factory}};
                            return api.Raw.External(41) == 42 && api.Raw.ExternalAlias(41) == 42 ? 42 : 1;
                        }
                    }
                    """, "OriginalConsumer", root, sourceRuntime: false));
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void CHeaderOptInIsExplicitAndDoesNotReconfigureTheCppBridge()
    {
        GenerationRequest request = new(Configuration(), Path.GetTempPath());
        Assert.False(request.configuration.source.includeHeaderHelpers);
        Assert.True(request.WithConfiguration(request.configuration with { source = request.configuration.source with { includeHeaderHelpers = true } })
            .configuration.source.includeHeaderHelpers);
        ArgumentException failure = Assert.Throws<ArgumentException>(() => request.WithConfiguration(request.configuration with
        { source = request.configuration.source with { includeHeaderHelpers = true, language = SourceLanguage.Cpp, languageStandard = null } }));
        Assert.Contains("source.includeHeaderHelpers", failure.Message, StringComparison.Ordinal);
    }

    private static BindingConfiguration Configuration() => new()
    {
        schemaVersion = 2, library = new() { id = "original", nativeName = "original_fixture" },
        source = new() { language = SourceLanguage.C, headers = ["library.h"], includeTransitivelyReferencedHeaders = false },
        api = new() { @namespace = "Generated.Original", name = "OriginalApi", names = ImmutableDictionary<string, string>.Empty.Add("EXTERNAL_ALIAS", "ExternalAlias") },
         cache = new() { enabled = false }
    };

    private static string WorkerRoot()
    {
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "BindGen-CS.slnx"))) repository = repository.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(repository!.FullName, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
