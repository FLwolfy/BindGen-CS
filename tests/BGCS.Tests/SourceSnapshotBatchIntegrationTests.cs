using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Core.Plugins;
using BGCS.Core.Ports;
using BGCS.Core.Requests;
using Xunit;

namespace BGCS.Tests;

public sealed class SourceSnapshotBatchIntegrationTests
{
    [Fact]
    public async Task RepeatedDirectoryDeclarationsStillRejectLateMembershipChangesAndPreservePublication()
    {
        using WorkerPluginFixture fixture = new();
        string directory = Directory.CreateDirectory(Path.Combine(fixture.root, "directory-input")).FullName;
        File.WriteAllText(Path.Combine(directory, "original.any"), "input");
        string added = Path.Combine(directory, "new.unrecognized-extension");
        bool mutate = false;
        await using BindingHost host = fixture.Builder().AddPluginFactory(DirectoryPlugin.Descriptor,
            () => new DirectoryPlugin(directory)).RegisterFactory<IGenerationValidator>(new("batch-mutation", ExtensionPort.GenerationValidator, 0), _ => new MutationValidator(() => { if (mutate) File.WriteAllText(added, "new membership"); })).Build();
        GenerationRequest request = fixture.Request();
        await Warm(host, request);
        Dictionary<string, byte[]> before = Snapshot(fixture.output);
        mutate = true;
        GenerationResult rejected = await host.GenerateAsync(request);
        Assert.False(rejected.success);
        Assert.Contains("external inputs changed", rejected.GetDiagnosticSummary());
        AssertSnapshot(fixture.output, before);
        mutate = false;
        File.Delete(added);
        GenerationResult recovered = await host.GenerateAsync(request);
        Assert.True(recovered.success, recovered.GetDiagnosticSummary());
        AssertHits(recovered);
    }

    [Fact]
    public async Task WarmWorkerPluginInputsRetainCompleteEvidenceAndRejectSameLengthLateMutation()
    {
        using WorkerPluginFixture fixture = new();
        bool mutate = false;
        await using BindingHost host = fixture.Builder().RegisterFactory<IGenerationValidator>(new("batch-mutation", ExtensionPort.GenerationValidator, 0), _ => new MutationValidator(() => { if (mutate) Rewrite(fixture.external, "other"); })).Build();
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


    private static async Task Warm(
        BindingHost host,
        GenerationRequest request
    ) {
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

    private static void Rewrite(
        string path,
        string value
    ) {
        DateTime timestamp = File.GetLastWriteTimeUtc(path);
        Assert.Equal(new FileInfo(path).Length, (long)System.Text.Encoding.UTF8.GetByteCount(value));
        File.WriteAllText(path, value);
        File.SetLastWriteTimeUtc(path, timestamp);
    }

    private sealed class MutationValidator(Action mutation) : IGenerationValidator
    {
        public string id => "batch-mutation";
        public int priority => 0;
        public ValueTask<ImmutableArray<BindingDiagnostic>> ValidateAsync(
            ValidationRequest request,
            CancellationToken cancellationToken
        ) {
            mutation();
            return ValueTask.FromResult(ImmutableArray<BindingDiagnostic>.Empty);
        }
    }

    private sealed class DirectoryPlugin(string directory) : IBindingPlugin, IExternalInputProvider, ICacheFingerprintProvider
    {
        internal static PluginDescriptor Descriptor { get; } = new("batch.directory", "1.0.0", [new("batch.directory.transform", ExtensionPort.NativeModuleTransform)]);
        public PluginDescriptor descriptor => Descriptor;
        public ImmutableArray<DeclaredExternalInput> GetExternalInputs(
            ExternalInputContext context,
            CancellationToken cancellationToken
        )
            => [new(directory, ExternalInputKind.Directory)];
        public CacheFingerprintContribution CaptureFingerprint(
            CacheFingerprintContext context,
            CancellationToken cancellationToken
        )
            => new(new ContentDigest(new string('a', 64)), [new(directory, ExternalInputKind.Directory)]);
        public void Configure(
            IExtensionRegistryBuilder registry,
            PluginConfiguration configuration
        )
            => registry.RegisterFactory<INativeModuleTransform>("batch.directory.transform", _ => new DirectoryTransform());
        public void Dispose() { }
    }

    private sealed class DirectoryTransform : INativeModuleTransform, ICacheFingerprintProvider
    {
        public string id => "batch.directory.transform";
        public int priority => 0;
        public NativeModule Transform(
            NativeModule module,
            AnalysisContext context,
            CancellationToken cancellationToken
        ) => module;
        public CacheFingerprintContribution CaptureFingerprint(
            CacheFingerprintContext context,
            CancellationToken cancellationToken
        )
            => new(new ContentDigest(new string('b', 64)), []);
    }

    private sealed class MutationState(
        string input,
        bool restore
    )
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
        public ImmutableArray<DeclaredExternalInput> GetExternalInputs(
            ExternalInputContext context,
            CancellationToken cancellationToken
        )
            => [new(state.path, ExternalInputKind.File)];
        public CacheFingerprintContribution CaptureFingerprint(
            CacheFingerprintContext context,
            CancellationToken cancellationToken
        )
            => new(new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(state.path)))), [new(state.path, ExternalInputKind.File)]);
        public void Configure(
            IExtensionRegistryBuilder registry,
            PluginConfiguration configuration
        ) {
            if (state.armed) Rewrite(state.path, "other");
            registry.RegisterFactory<INativeModuleTransform>("batch.restore", _ => new RestoreTransform(state));
        }
        public void Dispose() { }
    }

    private sealed class RestoreTransform(MutationState state) : INativeModuleTransform, ICacheFingerprintProvider
    {
        public string id => "batch.restore";
        public int priority => 0;
        public NativeModule Transform(
            NativeModule module,
            AnalysisContext context,
            CancellationToken cancellationToken
        ) {
            if (state.armed && state.restoreDuringTransform) Rewrite(state.path, "first");
            return module;
        }
        public CacheFingerprintContribution CaptureFingerprint(
            CacheFingerprintContext context,
            CancellationToken cancellationToken
        )
            => new(new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(state.path)))), [new(state.path, ExternalInputKind.File)]);
    }

    private static Dictionary<string, byte[]> Snapshot(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(root, path), File.ReadAllBytes, StringComparer.Ordinal);
    private static void AssertSnapshot(
        string root,
        Dictionary<string, byte[]> before
    ) {
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path)).Order(StringComparer.Ordinal));
        foreach ((string path, byte[] content) in before) Assert.Equal(content, File.ReadAllBytes(Path.Combine(root, path)));
    }
}
