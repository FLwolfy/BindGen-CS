using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Inputs;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeSourceProcessorTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false, false)]
    [InlineData(BindingImportMode.FunctionTable, true, true)]
    [InlineData(BindingImportMode.DllImport, false, true)]
    [InlineData(BindingImportMode.LibraryImport, true, false)]
    public async Task ConfiguredCandidatesComposePartialApiAndInvokeNativeWithoutPublishingAbiTampering(BindingImportMode mode, bool sourceRuntime, bool singleFile)
    {
        using Scratch scratch = new();
        BindingConfiguration configuration = Configuration(mode, sourceRuntime, singleFile) with { };
        await using BindingHost host = Builder().Build();
        GenerationResult result = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, scratch.root));
        Assert.True(result.success, result.GetDiagnosticSummary());
        var target = Assert.Single(result.targetModels);
        string targetRoot = Path.Combine(result.outputRoot, target.targetId);
        string managedRoot = Path.Combine(targetRoot, "managed");
        string[] managed = Directory.GetFiles(managedRoot, "*.cs", SearchOption.AllDirectories);
        if (singleFile) Assert.Equal(Path.Combine(managedRoot, "Complete.cs"), Assert.Single(managed));
        else Assert.Contains(managed, path => path == Path.Combine(managedRoot, "Extras", "ProcessorApi.Part.cs"));
        string combined = string.Join("\n", managed.Select(File.ReadAllText));
        Assert.Contains("return 20 + 1;", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("return 10;", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("return 20;", combined, StringComparison.Ordinal);
        string binary = Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(targetRoot)));
        string factory = mode == BindingImportMode.FunctionTable ? "ProcessorApi.Load(binary)" : "ProcessorApi.Create()";
        string probe = $$"""
            using Generated.Processors;
            public static class ProcessorProbe {
                public static int Run(string binary) {
                    using var api = {{factory}};
                    if (api.Raw.AddNative(10, 12) != 42) return 1;
                    if (api.Additional(9) != 42) return 2;
                    if (ProcessorApi.Marker() != 21) return 3;
                    return 42;
                }
            }
            """;
        Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(managedRoot, binary, configuration.library.nativeName,
            probe, "ProcessorProbe", scratch.root, sourceRuntime));
        if (mode == BindingImportMode.FunctionTable && !sourceRuntime && !singleFile)
        {
            string[] before = Files(result.outputRoot);
            SourceProcessorConfiguration tamper = new() { id = "reject.abi", priority = -1, kind = SourceProcessorKind.ReplaceText,
                paths = ["**/*.cs"], match = "Cdecl", replacement = "Stdcall" };
            GenerationResult rejected = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration with
            { output = configuration.output with { processors = configuration.output.processors.Add(tamper) } }, scratch.root));
            Assert.False(rejected.success);
            Assert.Contains(rejected.diagnostics, diagnostic => diagnostic.severity == BGCS.Contracts.BindingDiagnosticSeverity.Error);
            Assert.Equal(before, Files(result.outputRoot));
        }
    }

    [Fact]
    public async Task ConfiguredSameIdContentAndOrderChangesCannotReuseAnEarlierEmissionKey()
    {
        using Scratch scratch = new();
        BindingConfiguration configuration = Configuration(BindingImportMode.FunctionTable, false, false) with
        { output = Configuration(BindingImportMode.FunctionTable, false, false).output with { root = "CacheOutput" }};
        await using BindingHost host = Builder().Build();
        GenerationResult cold = await host.GenerateAsync(new(configuration, scratch.root));
        Assert.True(cold.success, cold.GetDiagnosticSummary());
        Assert.Equal("miss", Cache(cold));
        string[] coldBytes = ManagedBytes(cold);
        GenerationResult warm = await host.GenerateAsync(new(configuration, scratch.root));
        Assert.True(warm.success, warm.GetDiagnosticSummary());
        Assert.Equal("hit", Cache(warm));
        Assert.Equal(coldBytes, ManagedBytes(warm));
        SourceProcessorConfiguration[] changedRules = configuration.output.processors.ToArray();
        changedRules[2] = changedRules[2] with { replacement = "return ${value} + 2;" };
        BindingConfiguration changed = configuration with { output = configuration.output with { processors = [.. changedRules] } };
        GenerationResult edited = await host.GenerateAsync(new(changed, scratch.root));
        Assert.True(edited.success, edited.GetDiagnosticSummary());
        Assert.Equal("miss", Cache(edited));
        Assert.Contains("return 20 + 2;", File.ReadAllText(Path.Combine(edited.outputRoot, Assert.Single(edited.artifacts,
            artifact => artifact.relativePath.EndsWith("/Extras/ProcessorApi.Part.cs", StringComparison.Ordinal)).relativePath)), StringComparison.Ordinal);
        changedRules[2] = changedRules[2] with { priority = 25 };
        BindingConfiguration wrongOrder = configuration with { output = configuration.output with { processors = [.. changedRules] } };
        string[] preserved = Files(edited.outputRoot);
        GenerationResult failed = await host.GenerateAsync(new(wrongOrder, scratch.root));
        Assert.False(failed.success);
        Assert.Contains("matched no", failed.GetDiagnosticSummary(), StringComparison.Ordinal);
        Assert.Equal(preserved, Files(edited.outputRoot));
    }

    private static BindingConfiguration Configuration(BindingImportMode mode, bool sourceRuntime, bool singleFile) => new()
    {
        schemaVersion = 1, library = new() { id = "processors", nativeName = "processor_fixture" },
        source = new() { language = SourceLanguage.C, headers = ["native.h"], parseMacros = false, includeTransitivelyReferencedHeaders = false },
        api = new() { @namespace = "Generated.Processors", name = "ProcessorApi", importMode = mode,
            names = ImmutableDictionary<string, string>.Empty.Add("native_add", "AddNative") },
        runtime = new() { mode = sourceRuntime ? "source" : "package" },
        output = new() { layout = singleFile ? "singleFile" : "split", singleFileName = "Complete.cs", processors =
        [new() { id = "source.add", priority = 30, kind = SourceProcessorKind.AddSource, path = "Extras/ProcessorApi.Part.cs",
            content = "namespace Generated.Processors; public unsafe partial class ProcessorApi { public int Additional(int value) => Raw.AddNative(value, 15); public static int Marker() { return 10; } }" },
         new() { id = "source.literal", priority = 20, paths = ["Extras/ProcessorApi.Part.cs"], match = "return 10;", replacement = "return 20;" },
         new() { id = "source.regex", priority = 10, kind = SourceProcessorKind.RegexReplace, paths = ["Extras/*.cs"],
            match = "return (?<value>20);", replacement = "return ${value} + 1;", explicitCapture = true, timeoutMilliseconds = 2000 }]
        }
    };

    private static string Cache(GenerationResult result) => Assert.Single(result.cacheStages, stage => stage.stage == "csharpEmission").status;
    private static string[] ManagedBytes(GenerationResult result) => result.artifacts.Where(artifact => artifact.mediaType == "text/x-csharp")
        .OrderBy(artifact => artifact.relativePath, StringComparer.Ordinal)
        .Select(artifact => artifact.relativePath + ":" + Convert.ToBase64String(File.ReadAllBytes(Path.Combine(result.outputRoot, artifact.relativePath)))).ToArray();
    private static string[] Files(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).Select(path => Path.GetRelativePath(root, path) + ":" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)))).ToArray();
    private static BindingHostBuilder Builder() => BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot());
    private static string WorkerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(directory!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
    private sealed class Scratch : IDisposable
    {
        internal string root { get; } = Path.Combine(Path.GetTempPath(), "bgcs-configured-source-" + Guid.NewGuid().ToString("N"));
        internal Scratch()
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "native.h"), """
                #if defined(_WIN32)
                #define FIXTURE_EXPORT __declspec(dllexport)
                #else
                #define FIXTURE_EXPORT __attribute__((visibility("default")))
                #endif
                FIXTURE_EXPORT int native_add(int a, int b);
                """);
            File.WriteAllText(Path.Combine(root, "native.c"), "#include \"native.h\"\nFIXTURE_EXPORT int native_add(int a, int b) { return a * 3 + b; }\n");
        }
        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
