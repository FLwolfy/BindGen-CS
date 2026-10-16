using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Native;
using BGCS.Contracts.Plugins;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;
using Xunit;

namespace BGCS.Tests;

public sealed class GenerationStageCacheIntegrationTests
{
    [Fact]
    public async Task RealWorkerEmissionHitRestoresDeletedOwnedOutputAndStillRunsTheValidator()
    {
        using Scratch scratch = new();
        int validated = 0;
        await using BindingHost host = Builder().AddValidator("count-validation", 0, () => new CountingValidator(() => validated++)).Build();
        GenerationRequest request = new(Configuration(), scratch.root);
        GenerationResult cold = await host.GenerateAsync(request);
        Assert.True(cold.success, cold.GetDiagnosticSummary());
        Assert.Equal("miss", Assert.Single(cold.cacheStages.Where(stage => stage.stage == "csharpEmission")).status);
        var previous = ManagedBytes(cold);
        Assert.True(await host.ClearOwnedOutputAsync(request));
        GenerationResult hot = await host.GenerateAsync(request);
        Assert.True(hot.success, hot.GetDiagnosticSummary());
        Assert.Equal("hit", Assert.Single(hot.cacheStages.Where(stage => stage.stage == "csharpEmission")).status);
        Assert.Equal(2, validated);
        Assert.Equal(previous, ManagedBytes(hot));
    }

    [Fact]
    public async Task WarmEmissionDoesNotSkipRequestedManagedCompilationOrReplaceOutputOnFailure()
    {
        using Scratch scratch = new();
        string extra = Path.Combine(scratch.root, "extra.cs");
        await File.WriteAllTextAsync(extra, "internal sealed class AdditionalConsumer { }");
        BindingConfiguration configuration = Configuration() with { managedBuild = new() { sources = ["extra.cs"] } };
        await using BindingHost host = Builder().Build();
        BuildRequest request = new(new(configuration, scratch.root));
        BuildResult cold = await host.BuildAsync(request);
        Assert.True(cold.success, cold.GetDiagnosticSummary());
        byte[] report = File.ReadAllBytes(Path.Combine(cold.outputRoot, "generation.report.json"));
        await File.WriteAllTextAsync(extra, "this is invalid C# source");
        BuildResult failed = await host.BuildAsync(request);
        Assert.False(failed.success);
        Assert.Equal("miss", Assert.Single(failed.generation.cacheStages.Where(stage => stage.stage == "csharpEmission")).status);
        Assert.Contains(failed.diagnostics, diagnostic => diagnostic.code == "BGCS-MANAGED-COMPILE");
        Assert.Equal(report, File.ReadAllBytes(Path.Combine(cold.outputRoot, "generation.report.json")));
    }

    [Fact]
    public async Task CorruptCachedBytesAndChangedNativeModelAreRegeneratedBeforePublication()
    {
        using Scratch scratch = new();
        await using BindingHost host = Builder().Build();
        GenerationRequest request = new(Configuration(), scratch.root);
        GenerationResult cold = await host.GenerateAsync(request);
        Assert.True(cold.success, cold.GetDiagnosticSummary());
        var previous = ManagedBytes(cold);
        string entry = Assert.Single(Directory.GetFiles(Path.Combine(scratch.root, ".bgcs", "cache"), "*.stage.json"),
            path => !File.ReadAllText(path).Contains("frontend-result.json", StringComparison.Ordinal));
        JsonNode contents = JsonNode.Parse(await File.ReadAllTextAsync(entry))!;
        contents["artifacts"]![0]!["content"] = Convert.ToBase64String([1, 2, 3]);
        await File.WriteAllTextAsync(entry, contents.ToJsonString());
        GenerationResult repaired = await host.GenerateAsync(request);
        Assert.True(repaired.success, repaired.GetDiagnosticSummary());
        Assert.Equal("miss", Assert.Single(repaired.cacheStages.Where(stage => stage.stage == "csharpEmission")).status);
        Assert.Equal(previous, ManagedBytes(repaired));
        await File.WriteAllTextAsync(Path.Combine(scratch.root, "native.h"), "double add(double a, double b);");
        GenerationResult changed = await host.GenerateAsync(request);
        Assert.True(changed.success, changed.GetDiagnosticSummary());
        Assert.Equal("miss", Assert.Single(changed.cacheStages.Where(stage => stage.stage == "csharpEmission")).status);
        Assert.NotEqual(previous, ManagedBytes(changed));
    }

    [Fact]
    public async Task UnfingerprintedTransformAndLateProcessorDisableAffectedCacheReuse()
    {
        using Scratch scratch = new();
        int version = 1;
        await using BindingHost host = Builder().AddNativeTransform("unknown-state", 0, () => new UnknownStateTransform())
            .AddArtifactProcessor("late-patch", 0, () => new PatchProcessor(version)).Build();
        GenerationRequest request = new(Configuration(), scratch.root);
        GenerationResult first = await host.GenerateAsync(request);
        Assert.True(first.success, first.GetDiagnosticSummary());
        Assert.Equal("disabled", Assert.Single(first.cacheStages.Where(stage => stage.stage == "csharpEmission")).status);
        version = 2;
        GenerationResult second = await host.GenerateAsync(request);
        Assert.True(second.success, second.GetDiagnosticSummary());
        Assert.Equal("disabled", Assert.Single(second.cacheStages.Where(stage => stage.stage == "csharpEmission")).status);
        Assert.Contains("patch-version: 2", string.Join("\n", second.artifacts.Where(artifact => artifact.mediaType == "text/x-csharp").Select(artifact => File.ReadAllText(Path.Combine(second.outputRoot, artifact.relativePath)))));
    }

    [Fact]
    public async Task ExplicitUpstreamFingerprintsParticipateInStageKeys()
    {
        using Scratch scratch = new();
        int version = 1;
        await using BindingHost host = Builder().AddNativeTransform("state", 0, () => new FingerprintedTransform(version)).Build();
        GenerationRequest request = new(Configuration(), scratch.root);
        Assert.Equal("miss", Assert.Single((await host.GenerateAsync(request)).cacheStages.Where(stage => stage.stage == "csharpEmission")).status);
        Assert.Equal("hit", Assert.Single((await host.GenerateAsync(request)).cacheStages.Where(stage => stage.stage == "csharpEmission")).status);
        version = 2;
        Assert.Equal("miss", Assert.Single((await host.GenerateAsync(request)).cacheStages.Where(stage => stage.stage == "csharpEmission")).status);
    }

    [Fact]
    public async Task FingerprintedProcessorRunsOnHitsAndChangedPatchStateChangesTheStageKey()
    {
        using Scratch scratch = new();
        int version = 1;
        int processed = 0;
        await using BindingHost host = Builder().AddArtifactProcessor("fingerprinted-patch", 0,
            () => new FingerprintedPatch(version, () => processed++)).Build();
        GenerationRequest request = new(Configuration(), scratch.root);
        GenerationResult first = await host.GenerateAsync(request);
        Assert.True(first.success, first.GetDiagnosticSummary());
        Assert.Equal("miss", Assert.Single(first.cacheStages.Where(stage => stage.stage == "csharpEmission")).status);
        GenerationResult hot = await host.GenerateAsync(request);
        Assert.True(hot.success, hot.GetDiagnosticSummary());
        Assert.Equal("hit", Assert.Single(hot.cacheStages.Where(stage => stage.stage == "csharpEmission")).status);
        Assert.Equal(2, processed);
        version = 2;
        GenerationResult changed = await host.GenerateAsync(request);
        Assert.True(changed.success, changed.GetDiagnosticSummary());
        Assert.Equal("miss", Assert.Single(changed.cacheStages.Where(stage => stage.stage == "csharpEmission")).status);
        Assert.Contains("fingerprinted-version: 2", string.Join("\n", changed.artifacts.Where(artifact => artifact.mediaType == "text/x-csharp")
            .Select(artifact => File.ReadAllText(Path.Combine(changed.outputRoot, artifact.relativePath)))));
    }

    [Fact]
    public async Task DiffDoesNotTreatAColdToWarmCacheReceiptAsAContentChange()
    {
        using Scratch scratch = new();
        await using BindingHost host = Builder().Build();
        GenerationRequest request = new(Configuration(), scratch.root);
        GenerationResult generated = await host.GenerateAsync(request);
        Assert.True(generated.success, generated.GetDiagnosticSummary());
        GenerationDiffResult unchanged = await host.DiffAsync(request);
        Assert.True(unchanged.success);
        Assert.False(unchanged.hasChanges, string.Join(", ", unchanged.changes.Select(change => change.relativePath)));
        Assert.True(File.Exists(Path.Combine(generated.outputRoot, "generation.report.json")));
        await File.WriteAllTextAsync(Path.Combine(scratch.root, "native.h"), "double add(double a, double b);");
        GenerationDiffResult changed = await host.DiffAsync(request);
        Assert.True(changed.success);
        Assert.True(changed.hasChanges);
        Assert.Contains(changed.changes, artifact => artifact.relativePath.EndsWith(".g.cs", StringComparison.Ordinal));
    }

    private static BindingHostBuilder Builder() => BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot());
    private static BindingConfiguration Configuration() => new()
    {
        schemaVersion = 1, library = new() { id = "cache_fixture", nativeName = "cache_fixture" },
        source = new() { headers = ["native.h"], parseMacros = false }, api = new() { @namespace = "Generated.Cache", name = "CacheApi" }
    };
    private static string[] ManagedBytes(GenerationResult result) => result.artifacts.Where(artifact => artifact.mediaType == "text/x-csharp")
        .OrderBy(artifact => artifact.relativePath, StringComparer.Ordinal).Select(artifact => artifact.relativePath + ":" + Convert.ToBase64String(File.ReadAllBytes(Path.Combine(result.outputRoot, artifact.relativePath)))).ToArray();
    private static string WorkerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(directory!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
    private sealed class CountingValidator(Action validate) : IGenerationValidator
    {
        public string id => "count-validation";
        public int priority => 0;
        public ValueTask<ImmutableArray<BindingDiagnostic>> ValidateAsync(ValidationRequest request, CancellationToken cancellationToken)
        { validate(); return ValueTask.FromResult(ImmutableArray<BindingDiagnostic>.Empty); }
    }
    private sealed class UnknownStateTransform : INativeModuleTransform
    {
        public string id => "unknown-state";
        public int priority => 0;
        public NativeModule Transform(NativeModule module, AnalysisContext context, CancellationToken cancellationToken) => module;
    }
    private sealed class FingerprintedTransform(int version) : INativeModuleTransform, ICacheFingerprintProvider
    {
        public string id => "state";
        public int priority => 0;
        public NativeModule Transform(NativeModule module, AnalysisContext context, CancellationToken cancellationToken) => module;
        public CacheFingerprintContribution CaptureFingerprint(CacheFingerprintContext context, CancellationToken cancellationToken)
            => new(new ContentDigest(version.ToString("x64")), []);
    }
    private sealed class PatchProcessor(int version) : IArtifactProcessor
    {
        public string id => "late-patch";
        public int priority => 0;
        public async ValueTask ProcessAsync(ArtifactProcessingRequest request, CancellationToken cancellationToken)
        {
            ArtifactDescriptor artifact = request.artifacts.First(artifact => artifact.mediaType == "text/x-csharp");
            string text = await request.content.ReadTextAsync(artifact.relativePath, cancellationToken);
            await request.content.ReplaceTextAsync(artifact.relativePath, text + "\n// patch-version: " + version + "\n", cancellationToken);
        }
    }
    private sealed class FingerprintedPatch(int version, Action processed) : IArtifactProcessor, ICacheFingerprintProvider
    {
        public string id => "fingerprinted-patch";
        public int priority => 0;
        public CacheFingerprintContribution CaptureFingerprint(CacheFingerprintContext context, CancellationToken cancellationToken)
            => new(new ContentDigest(version.ToString("x64")), []);
        public async ValueTask ProcessAsync(ArtifactProcessingRequest request, CancellationToken cancellationToken)
        {
            processed();
            ArtifactDescriptor artifact = request.artifacts.First(artifact => artifact.mediaType == "text/x-csharp");
            string text = await request.content.ReadTextAsync(artifact.relativePath, cancellationToken);
            await request.content.ReplaceTextAsync(artifact.relativePath, text + "\n// fingerprinted-version: " + version + "\n", cancellationToken);
        }
    }
    private sealed class Scratch : IDisposable
    {
        internal string root { get; } = Path.Combine(Path.GetTempPath(), "bgcs-cache-host-" + Guid.NewGuid().ToString("N"));
        internal Scratch() { Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root, "native.h"), "int add(int a, int b);"); }
        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
