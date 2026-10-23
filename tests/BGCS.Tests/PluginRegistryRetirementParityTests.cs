using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using BGCS.Core.Plugins;
using BGCS.Core.Ports;
using BGCS.Core.Requests;
using BGCS.Core.Versioning;
using Xunit;

namespace BGCS.Tests;

public sealed class PluginRegistryRetirementParityTests
{
    [Theory]
    [InlineData(ContractVersion.C_MAJOR - 1, 0)]
    [InlineData(ContractVersion.C_MAJOR, ContractVersion.C_API_MINOR + 1)]
    [InlineData(ContractVersion.C_MAJOR + 1, 0)]
    [InlineData(ContractVersion.C_MAJOR, -1)]
    public void IncompatibleContractDescriptorsAreRejectedBeforeExecutingTheFactory(
        int major,
        int minimumMinor
    ) {
        int calls = 0;
        PluginDescriptor descriptor = new("probe.plugin", "1.0.0", [new("probe.service", ExtensionPort.NativeModuleTransform)])
        { contractsMajor = major, minimumApiMinor = minimumMinor };
        Assert.Throws<InvalidOperationException>(() => Builder().AddPluginFactory(descriptor, () => { calls++; throw new InvalidOperationException(); }));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void CurrentContractDescriptorIsAcceptedWithoutExecutingTheFactory()
    {
        int calls = 0;
        PluginDescriptor descriptor = new("probe.plugin", "1.0.0", [new("probe.service", ExtensionPort.NativeModuleTransform)])
        { contractsMajor = ContractVersion.C_MAJOR, minimumApiMinor = ContractVersion.C_API_MINOR };
        Builder().AddPluginFactory(descriptor, () => { calls++; throw new InvalidOperationException(); });
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task RejectedConflictingPluginDoesNotReserveItsOtherServiceOrPublishAnyFactory()
    {
        using Scratch scratch = new();
        List<string> trace = [];
        int rejectedActivations = 0;
        BindingHostBuilder builder = Builder().RegisterFactory<INativeModuleTransform>(new("existing", ExtensionPort.NativeModuleTransform, 1), _ => new Traced("existing", 1, trace));
        PluginDescriptor rejected = new("candidate", "1.0.0", [new("available", ExtensionPort.NativeModuleTransform), new("existing", ExtensionPort.NativeModuleTransform)]);
        Assert.Throws<InvalidOperationException>(() => builder.AddPluginFactory(rejected, () => { rejectedActivations++; throw new InvalidOperationException(); }));
        PluginDescriptor accepted = new("candidate", "1.0.0", [new("available", ExtensionPort.NativeModuleTransform, 2)]);
        builder.AddPluginFactory(accepted, () => new DeclaredPlugin(accepted, registry => registry.RegisterFactory<INativeModuleTransform>("available", _ => new Traced("available", 2, trace))));
        builder.RegisterFactory<INativeModuleTransform>(new("z-first", ExtensionPort.NativeModuleTransform, 10), _ => new Traced("z-first", 10, trace));
        builder.RegisterFactory<INativeModuleTransform>(new("a-first", ExtensionPort.NativeModuleTransform, 10), _ => new Traced("a-first", 10, trace));
        await using BindingHost host = builder.Build();
        AnalysisResult result = await host.AnalyzeAsync(AnalysisRequest.FromFiles([scratch.header]));
        Assert.True(result.success, result.GetDiagnosticSummary());
        Assert.Equal(0, rejectedActivations);
        Assert.Equal(new[] { "a-first", "z-first", "available", "existing" }, trace);
    }

    [Fact]
    public async Task ChangedDirectServiceStateInvalidatesAFormerEmissionCacheEntry()
    {
        using Scratch scratch = new();
        string state = "first";
        await using BindingHost host = Builder().RegisterFactory<INativeModuleTransform>(new("mutable", ExtensionPort.NativeModuleTransform, 0), _ => new Fingerprinted("mutable", () => state)).Build();
        GenerationRequest request = scratch.Request();
        Assert.Equal("miss", await Emit(host, request));
        Assert.Equal("hit", await Emit(host, request));
        state = "changed";
        Assert.Equal("miss", await Emit(host, request));
        Assert.Equal("hit", await Emit(host, request));
    }

    [Fact]
    public async Task MissingDirectServiceFingerprintRunsTheServiceAndDisablesEmissionCaching()
    {
        using Scratch scratch = new();
        List<string> trace = [];
        await using BindingHost host = Builder().RegisterFactory<INativeModuleTransform>(new("untracked", ExtensionPort.NativeModuleTransform, 0), _ => new Traced("untracked", 0, trace)).Build();
        Assert.Equal("disabled", await Emit(host, scratch.Request()));
        Assert.Equal("disabled", await Emit(host, scratch.Request()));
        Assert.Equal(new[] { "untracked", "untracked" }, trace);
    }

    [Fact]
    public async Task ExactNeutralPortIdentityChangesCacheKeysEvenWithTheSameClassIdAndState()
    {
        using Scratch scratch = new();
        GenerationRequest request = scratch.Request();
        await using (BindingHost native = Builder().RegisterFactory<INativeModuleTransform>(new("same", ExtensionPort.NativeModuleTransform, 0), _ => new Fingerprinted("same", () => "unchanged")).Build())
        {
            Assert.Equal("miss", await Emit(native, request));
            Assert.Equal("hit", await Emit(native, request));
        }
        await using BindingHost interop = Builder().RegisterFactory<IInteropTransform>(new("same", ExtensionPort.InteropTransform, 0), _ => new Fingerprinted("same", () => "unchanged")).Build();
        Assert.Equal("miss", await Emit(interop, request));
        Assert.Equal("hit", await Emit(interop, request));
    }

    [Fact]
    public async Task ThrowingPluginFingerprintDisposesTheSessionAndPreservesTheCompletePreviousPublication()
    {
        using Scratch scratch = new();
        GenerationRequest request = scratch.Request();
        await using (BindingHost original = Builder().Build()) _ = await Emit(original, request);
        Dictionary<string, byte[]> before = Directory.GetFiles(scratch.output, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(scratch.output, path), File.ReadAllBytes, StringComparer.Ordinal);
        List<BrokenFingerprint> instances = [];
        PluginDescriptor descriptor = new("broken", "1.0.0", [new("broken.service", ExtensionPort.NativeModuleTransform)]);
        await using BindingHost rejected = Builder().AddPluginFactory(descriptor, () => { BrokenFingerprint plugin = new(descriptor); instances.Add(plugin); return plugin; }).Build();
        for (int attempt = 0; attempt < 2; attempt++)
        {
            GenerationResult result = await rejected.GenerateAsync(request);
            Assert.False(result.success);
            Assert.Contains("cannot produce a fingerprint", result.GetDiagnosticSummary(), StringComparison.Ordinal);
            Assert.Equal(before.Keys.Order(StringComparer.Ordinal), Directory.GetFiles(scratch.output, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(scratch.output, path)).Order(StringComparer.Ordinal));
            foreach ((string file, byte[] bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(scratch.output, file)));
        }
        Assert.Equal(2, instances.Count);
        Assert.All(instances, instance => Assert.True(instance.disposed));
    }

    private static BindingHostBuilder Builder() => BindingGenerator.CreateBuilder().UseDetachedFrontend(() => new EmptyFrontend());
    private static async Task<string> Emit(
        BindingHost host,
        GenerationRequest request
    ) {
        GenerationResult result = await host.GenerateAsync(request);
        Assert.True(result.success, result.GetDiagnosticSummary());
        return Assert.Single(result.cacheStages, stage => stage.stage == "csharpEmission").status;
    }
    private static CacheFingerprintContribution Fingerprint(string value)
        => new(new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))), []);

    private sealed class EmptyFrontend : IFrontend, ICacheFingerprintProvider
    {
        public ValueTask<FrontendPreparation> PrepareAsync(
            FrontendRequest request,
            CancellationToken cancellationToken
        ) {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new FrontendPreparation(request, new([], []), []));
        }

        public ValueTask<FrontendResult> ParseAsync(
            FrontendRequest request,
            CancellationToken cancellationToken
        )
            => ValueTask.FromResult(new FrontendResult(new(request.target.targetId.value, request.target.triple,
                request.target.architectureId is "x86" or "wasm32" or "arm" ? 4 : 8, [], [], [])
            { language = request.language, byteOrder = request.target.byteOrder }, new([], [])));
        public CacheFingerprintContribution CaptureFingerprint(
            CacheFingerprintContext context,
            CancellationToken cancellationToken
        ) => Fingerprint("empty-native-facts-v1");
    }
    private sealed class Traced(
        string identity,
        int order,
        List<string> trace
    ) : INativeModuleTransform
    {
        public string id => identity;
        public int priority => order;
        public NativeModule Transform(
            NativeModule module,
            AnalysisContext context,
            CancellationToken cancellationToken
        ) { trace.Add(identity); return module; }
    }
    private sealed class Fingerprinted(
        string identity,
        Func<string> state
    ) : INativeModuleTransform, IInteropTransform, ICacheFingerprintProvider
    {
        public string id => identity;
        public int priority => 0;
        public NativeModule Transform(
            NativeModule module,
            AnalysisContext context,
            CancellationToken cancellationToken
        ) => module;
        public InteropModule Transform(
            InteropModule module,
            AnalysisContext context,
            CancellationToken cancellationToken
        ) => module;
        public CacheFingerprintContribution CaptureFingerprint(
            CacheFingerprintContext context,
            CancellationToken cancellationToken
        ) => Fingerprint(state());
    }
    private sealed class DeclaredPlugin(
        PluginDescriptor metadata,
        Action<IExtensionRegistryBuilder> configure
    ) : IBindingPlugin
    {
        public PluginDescriptor descriptor => metadata;
        public void Configure(
            IExtensionRegistryBuilder registry,
            PluginConfiguration configuration
        ) => configure(registry);
    }
    private sealed class BrokenFingerprint(PluginDescriptor metadata) : IBindingPlugin, ICacheFingerprintProvider
    {
        internal bool disposed { get; private set; }
        public PluginDescriptor descriptor => metadata;
        public void Configure(
            IExtensionRegistryBuilder registry,
            PluginConfiguration configuration
        )
            => registry.RegisterFactory<INativeModuleTransform>("broken.service", _ => new Fingerprinted("broken.service", () => "fixed"));
        public CacheFingerprintContribution CaptureFingerprint(
            CacheFingerprintContext context,
            CancellationToken cancellationToken
        )
            => throw new InvalidOperationException("The configured plugin state cannot produce a fingerprint.");
        public void Dispose() => disposed = true;
    }
    private sealed class Scratch : IDisposable
    {
        internal string root { get; } = Path.Combine(Path.GetTempPath(), "bgcs-plugin-retirement-" + Guid.NewGuid().ToString("N"));
        internal string header => Path.Combine(root, "native.h");
        internal string output => Path.Combine(root, "Generated");
        internal Scratch() { Directory.CreateDirectory(root); File.WriteAllText(header, "/* Detached empty neutral facts are intentional. */"); }
        internal GenerationRequest Request() => new(new BindingConfiguration
        {
            schemaVersion = 2,
            library = new() { id = "plugin_retirement", nativeName = "plugin_retirement" },
            source = new() { headers = [header], parseMacros = false },
            api = new() { @namespace = "Generated.PluginRetirement", name = "PluginApi" }
        }, root);
        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
