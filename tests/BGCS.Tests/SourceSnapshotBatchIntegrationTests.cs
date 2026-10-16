using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.Contracts.Plugins;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;
using Xunit;

namespace BGCS.Tests;

public sealed class SourceSnapshotBatchIntegrationTests
{
    [Fact]
    public async Task WarmWorkerPluginInputsRetainCompleteEvidenceAndRejectSameLengthLateMutation()
    {
        using WorkerPluginFixture fixture = new();
        bool mutate = false;
        await using BindingHost host = fixture.Builder().AddValidator("batch-mutation", 0,
            () => new MutationValidator(() => { if (mutate) Rewrite(fixture.external, "other"); })).Build();
        GenerationRequest request = fixture.Request();
        await Warm(host, request);
        string proof = Directory.GetFiles(fixture.output, "verification.json", SearchOption.AllDirectories).Single();
        using (JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(proof)))
        {
            var dependencies = document.RootElement.GetProperty("extensionInputs").EnumerateArray()
                .SelectMany(snapshot => snapshot.GetProperty("files").EnumerateArray())
                .Where(input => input.GetProperty("path").GetString() == fixture.external).ToArray();
            Assert.True(dependencies.Length >= 2, "The worker capture and detached execution closure must both retain their original input evidence.");
            Assert.Single(dependencies.Select(input => input.GetProperty("digest").GetProperty("value").GetString()).Distinct());
        }
        Dictionary<string, byte[]> before = Snapshot(fixture.output);
        DateTime timestamp = File.GetLastWriteTimeUtc(fixture.external);
        mutate = true;
        GenerationResult rejected = await host.GenerateAsync(request);
        Assert.False(rejected.success);
        Assert.Contains("external inputs changed", rejected.GetDiagnosticSummary());
        Assert.Equal("other", File.ReadAllText(fixture.external));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(fixture.external));
        AssertSnapshot(fixture.output, before);
        mutate = false;
        Rewrite(fixture.external, "first");
        GenerationResult recovered = await host.GenerateAsync(request);
        Assert.True(recovered.success, recovered.GetDiagnosticSummary());
        AssertHits(recovered);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContradictorySamePathSnapshotsRejectEitherCurrentDigestBeforeObservationReplay(bool restoreDuringTransform)
    {
        using WorkerPluginFixture fixture = new();
        MutationState state = new(fixture.external, restoreDuringTransform);
        await using BindingHost host = fixture.Builder().AddPluginFactory(ContradictoryPlugin.Descriptor,
            () => new ContradictoryPlugin(state)).Build();
        GenerationRequest request = fixture.Request();
        await Warm(host, request);
        Dictionary<string, byte[]> before = Snapshot(fixture.output);
        state.armed = true;
        GenerationResult rejected = await host.GenerateAsync(request);
        Assert.False(rejected.success);
        Assert.Contains("An extension's declared external inputs changed", rejected.GetDiagnosticSummary());
        Assert.Equal(restoreDuringTransform ? "first" : "other", File.ReadAllText(fixture.external));
        AssertSnapshot(fixture.output, before);
    }

    [Fact]
    public async Task WarmCandidateStillReplaysUntrackedAbsoluteHeaderQueriesBeforePublication()
    {
        using WorkerPluginFixture fixture = new();
        string directory = Directory.CreateDirectory(Path.Combine(fixture.root, "absolute-query")).FullName;
        string optional = Path.Combine(directory, "feature.h");
        string literal = optional.Replace('\\', '/');
        File.WriteAllText(Path.Combine(fixture.source, "native.h"), $$"""
            #if __has_include("{{literal}}")
            double read_value(void);
            #else
            int read_value(void);
            #endif
            """);
        bool mutate = false;
        await using BindingHost host = fixture.Builder().AddValidator("batch-mutation", 0,
            () => new MutationValidator(() => { if (mutate) File.WriteAllText(optional, "/* newly observed absolute input */\n"); })).Build();
        GenerationRequest request = fixture.Request();
        await Warm(host, request);
        Dictionary<string, byte[]> before = Snapshot(fixture.output);
        mutate = true;
        GenerationResult rejected = await host.GenerateAsync(request);
        Assert.False(rejected.success);
        Assert.Contains("Native compiler source-view observations changed", rejected.GetDiagnosticSummary());
        Assert.True(File.Exists(optional));
        AssertSnapshot(fixture.output, before);
        mutate = false;
        File.Delete(optional);
        GenerationResult recovered = await host.GenerateAsync(request);
        Assert.True(recovered.success, recovered.GetDiagnosticSummary());
        AssertHits(recovered);
    }

    private static async Task Warm(BindingHost host, GenerationRequest request)
    {
        GenerationResult cold = await host.GenerateAsync(request);
        Assert.True(cold.success, cold.GetDiagnosticSummary());
        Assert.All(cold.cacheStages, stage => Assert.Equal("miss", stage.status));
        GenerationResult warm = await host.GenerateAsync(request);
        Assert.True(warm.success, warm.GetDiagnosticSummary());
        AssertHits(warm);
    }

    private static void AssertHits(GenerationResult result)
    {
        Assert.Equal("hit", Assert.Single(result.cacheStages, stage => stage.stage == "primaryFrontend").status);
        Assert.Equal("hit", Assert.Single(result.cacheStages, stage => stage.stage == "csharpEmission").status);
    }

    private static void Rewrite(string path, string value)
    {
        DateTime timestamp = File.GetLastWriteTimeUtc(path);
        Assert.Equal(new FileInfo(path).Length, (long)System.Text.Encoding.UTF8.GetByteCount(value));
        File.WriteAllText(path, value);
        File.SetLastWriteTimeUtc(path, timestamp);
    }

    private sealed class MutationValidator(Action mutation) : IGenerationValidator
    {
        public string id => "batch-mutation";
        public int priority => 0;
        public ValueTask<ImmutableArray<BindingDiagnostic>> ValidateAsync(ValidationRequest request, CancellationToken cancellationToken)
        {
            mutation();
            return ValueTask.FromResult(ImmutableArray<BindingDiagnostic>.Empty);
        }
    }

    private sealed class MutationState(string input, bool restore)
    {
        internal string path => input;
        internal bool restoreDuringTransform => restore;
        internal bool armed { get; set; }
    }

    private sealed class ContradictoryPlugin(MutationState state) : IBindingPlugin, IExternalInputProvider, ICacheFingerprintProvider
    {
        internal static PluginDescriptor Descriptor { get; } = new("batch.contradiction", "1.0.0",
            [new("batch.restore", ExtensionPort.NativeModuleTransform)]);
        public PluginDescriptor descriptor => Descriptor;
        public ImmutableArray<DeclaredExternalInput> GetExternalInputs(ExternalInputContext context, CancellationToken cancellationToken)
            => [new(state.path, ExternalInputKind.File)];
        public CacheFingerprintContribution CaptureFingerprint(CacheFingerprintContext context, CancellationToken cancellationToken)
            => new(new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(state.path)))), [new(state.path, ExternalInputKind.File)]);
        public void Configure(IExtensionRegistryBuilder registry, PluginConfiguration configuration)
        {
            if (state.armed) Rewrite(state.path, "other");
            registry.RegisterFactory<INativeModuleTransform>("batch.restore", () => new RestoreTransform(state));
        }
        public void Dispose() { }
    }

    private sealed class RestoreTransform(MutationState state) : INativeModuleTransform, ICacheFingerprintProvider
    {
        public string id => "batch.restore";
        public int priority => 0;
        public NativeModule Transform(NativeModule module, AnalysisContext context, CancellationToken cancellationToken)
        {
            if (state.armed && state.restoreDuringTransform) Rewrite(state.path, "first");
            return module;
        }
        public CacheFingerprintContribution CaptureFingerprint(CacheFingerprintContext context, CancellationToken cancellationToken)
            => new(new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(state.path)))), [new(state.path, ExternalInputKind.File)]);
    }

    private static Dictionary<string, byte[]> Snapshot(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(root, path), File.ReadAllBytes, StringComparer.Ordinal);
    private static void AssertSnapshot(string root, Dictionary<string, byte[]> before)
    {
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path)).Order(StringComparer.Ordinal));
        foreach ((string path, byte[] content) in before) Assert.Equal(content, File.ReadAllBytes(Path.Combine(root, path)));
    }
}
