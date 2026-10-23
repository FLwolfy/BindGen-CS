using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using BGCS.Configuration;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Core.Plugins;
using BGCS.Core.Ports;
using BGCS.Core.Requests;
using BGCS.Core.Targets;
using BGCS.Core.Hosting;
using BGCS.Hosting.Standard;
using BGCS.Targets.Desktop;
using BGCS.Interop.DotNet;
using BGCS.Core;
using Xunit;

namespace BGCS.Tests;

public sealed class PluginSessionTests
{
    [Fact]
    public async Task PredeclaredPluginMetadata_DoesNotExecuteTheFactoryBeforeItsSession()
    {
        List<ProbePlugin> plugins = [];
        PluginDescriptor descriptor = new("probe", "1.0.0", [new("probe.transform", ExtensionPort.NativeModuleTransform, 4)]);
        BindingHostBuilder builder = BindingGenerator.CreateBuilder().UseDetachedFrontend(() => new EmptyFrontend())
            .AddPluginFactory(descriptor, () => { ProbePlugin plugin = new(); plugins.Add(plugin); return plugin; });
        Assert.Empty(plugins);
        await using BindingHost host = builder.Build();
        Assert.Empty(plugins);
        AnalysisResult result = await host.AnalyzeAsync(Request());
        Assert.True(result.success, result.GetDiagnosticSummary());
        ProbePlugin created = Assert.Single(plugins);
        Assert.True(created.disposed);
        Assert.Equal(new[] { "configure", "create", "transform", "service.dispose", "plugin.dispose" }, created.trace);
    }

    [Fact]
    public async Task DiscoveryAndSessions_HaveIndependentPluginsAndReleaseServicesBeforeTheirPlugin()
    {
        List<ProbePlugin> plugins = [];
        BindingHostBuilder builder = BindingGenerator.CreateBuilder().UseDetachedFrontend(() => new EmptyFrontend())
            .AddPluginFactory(() => { ProbePlugin plugin = new(); plugins.Add(plugin); return plugin; });
        Assert.True(Assert.Single(plugins).disposed);
        await using BindingHost host = builder.Build();
        Assert.True((await host.AnalyzeAsync(Request())).success);
        Assert.True((await host.AnalyzeAsync(Request())).success);
        Assert.Equal(3, plugins.Count);
        Assert.DoesNotContain(plugins[0].trace, item => item != "plugin.dispose");
        foreach (ProbePlugin plugin in plugins.Skip(1))
        {
            Assert.Equal(new[] { "configure", "create", "transform", "service.dispose", "plugin.dispose" }, plugin.trace);
            Assert.Throws<InvalidOperationException>(() => plugin.retainedRegistry!.RegisterFactory<INativeModuleTransform>("probe.transform", _ => null!));
        }
    }

    [Fact]
    public void DuplicatePluginAndGlobalServiceIdsAndPrimaryPorts_AreRejectedAtHostFreeze()
    {
        BindingHostBuilder builder = BindingGenerator.CreateBuilder().AddPlugin<ProbePlugin>();
        Assert.Throws<InvalidOperationException>(() => builder.AddPlugin<ProbePlugin>());
        Assert.Throws<InvalidOperationException>(() => builder.RegisterFactory<INativeTypeLowering>(new("probe.transform", ExtensionPort.NativeTypeLowering, 0), _ => null!));
        Assert.Throws<InvalidOperationException>(() => BindingGenerator.CreateBuilder().UseDetachedFrontend(() => new EmptyFrontend()).AddPlugin<FrontendPlugin>().Build());
        Assert.Throws<InvalidOperationException>(() => BindingGenerator.CreateBuilder().AddPluginFactory(() => new ProbePlugin(major: BGCS.Core.Versioning.ContractVersion.C_MAJOR + 1)));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong-port")]
    [InlineData("undeclared")]
    [InlineData("duplicate")]
    [InlineData("changing")]
    public async Task InvalidSessionRegistration_ReleasesThePluginAndNeverCreatesTheFrontend(string failure)
    {
        List<ProbePlugin> plugins = [];
        bool frontendCreated = false;
        await using BindingHost host = BindingGenerator.CreateBuilder()
            .UseDetachedFrontend(() => { frontendCreated = true; return new EmptyFrontend(); })
            .AddPluginFactory(() => { ProbePlugin plugin = new(failure); plugins.Add(plugin); return plugin; }).Build();
        AnalysisResult result = await host.AnalyzeAsync(Request());
        Assert.False(result.success);
        Assert.False(frontendCreated);
        Assert.Equal(2, plugins.Count);
        Assert.All(plugins, plugin => Assert.True(plugin.disposed));
    }

    [Fact]
    public async Task PrimaryFrontendAndCustomTarget_FromNeutralPluginsUseTheSameGenerationPipeline()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-plugin-target-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "custom.h"), "/* Explicit neutral frontend fixture input. */");
        try
        {
            await using BindingHost host = PluginFrontendBuilder().AddPlugin<TargetPlugin>().Build();
            BindingConfiguration configuration = new()
            {
                schemaVersion = 2, library = new() { id = "custom", nativeName = "custom" }, source = new() { headers = ["custom.h"] },
                targets = [new() { id = "custom-wasm32" }], api = new() { runtime = new("fixture.empty-model") }, cache = new() { enabled = false }
            };
            GenerationResult result = await host.GenerateAsync(new(configuration, root));
            Assert.True(result.success, result.GetDiagnosticSummary());
            TargetInspection inspected = Assert.Single(result.targetModels);
            Assert.Equal("custom-wasm32", inspected.native.targetId);
            Assert.Equal(4, inspected.native.pointerSize);
            Assert.Equal(NativeByteOrder.LittleEndian, inspected.native.byteOrder);
            Assert.True(File.Exists(Path.Combine(result.outputRoot, "custom-wasm32", "metadata", "abi.manifest.json")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task CustomBrowserTargetWithoutByteOrder_CannotPublishAnAssumedAbi()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-plugin-target-unknown-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "custom.h"), "/* Explicit neutral frontend fixture input. */");
        try
        {
            await using BindingHost host = PluginFrontendBuilder()
                .RegisterFactory<INativeTargetProvider>(new("probe.unknown-wasm", ExtensionPort.NativeTargetProvider), _ => new CustomTarget(() => { }, NativeByteOrder.Unknown)).Build();
            BindingConfiguration configuration = new()
            {
                schemaVersion = 2, library = new() { id = "custom", nativeName = "custom" }, source = new() { headers = ["custom.h"] },
                targets = [new() { id = "custom-wasm32" }], api = new() { runtime = new("fixture.empty-model") }, cache = new() { enabled = false }
            };
            GenerationResult result = await host.GenerateAsync(new(configuration, root));
            Assert.False(result.success);
            Assert.False(result.published);
            Assert.Contains("byte order", result.GetDiagnosticSummary(), StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(result.outputRoot));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task PluginCleanupFailure_BlocksPublicationEvenWhenAllStagesSucceeded()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-plugin-release-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "custom.h"), "/* Empty neutral frontend fixture. */");
        int instances = 0;
        try
        {
            await using BindingHost host = PluginFrontendBuilder()
                .AddPluginFactory(() => new ProbePlugin(throwOnDispose: ++instances > 1)).Build();
            GenerationResult result = await host.GenerateAsync(new(new()
            {
                schemaVersion = 2, library = new() { id = "release", nativeName = "release" }, source = new() { headers = ["custom.h"] },
                targets = [new() { id = "host" }], cache = new() { enabled = false }
            }, root));
            Assert.False(result.success);
            Assert.Contains("plugin cleanup", result.GetDiagnosticSummary(), StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(result.outputRoot));
            Assert.Empty(Directory.GetDirectories(root, ".bgcs-staging-*"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ConflictingTargetClaims_ReleaseAllProviderInstances()
    {
        int disposed = 0;
        await using BindingHost host = BindingGenerator.CreateBuilder().UseDetachedFrontend(() => new EmptyFrontend())
            .RegisterFactory<INativeTargetProvider>(new("target.first", ExtensionPort.NativeTargetProvider), _ => new CustomTarget(() => disposed++))
            .RegisterFactory<INativeTargetProvider>(new("target.second", ExtensionPort.NativeTargetProvider), _ => new CustomTarget(() => disposed++)).Build();
        AnalysisResult result = await host.AnalyzeAsync(Request() with { targetId = "custom-wasm32" });
        Assert.False(result.success);
        Assert.Contains("Multiple target providers claim", result.GetDiagnosticSummary());
        Assert.Equal(2, disposed);
    }

    [Fact]
    public async Task PluginSettings_AreSchemaCheckedAndDetachedFromTheCallerDocument()
    {
        List<SettingsPlugin> plugins = [];
        using JsonDocument document = JsonDocument.Parse("{\"enabled\":false,\"count\":0}");
        BindingHostBuilder builder = BindingGenerator.CreateBuilder().UseDetachedFrontend(() => new EmptyFrontend())
            .AddPluginFactory(() => { SettingsPlugin plugin = new(); plugins.Add(plugin); return plugin; }, document.RootElement, AppContext.BaseDirectory);
        document.Dispose();
        await using BindingHost host = builder.Build();
        AnalysisResult result = await host.AnalyzeAsync(Request());
        Assert.True(result.success, result.GetDiagnosticSummary());
        Assert.Equal(2, plugins.Count);
        Assert.False(plugins[1].configuration!.settings.GetProperty("enabled").GetBoolean());
        Assert.Equal(0, plugins[1].configuration!.settings.GetProperty("count").GetInt32());
        Assert.Equal(Path.GetFullPath(AppContext.BaseDirectory), plugins[1].configuration!.pathBase);
        using JsonDocument invalid = JsonDocument.Parse("{\"enabled\":false,\"count\":0,\"typo\":true}");
        Assert.Throws<ArgumentException>(() => BindingGenerator.CreateBuilder().AddPlugin<SettingsPlugin>(invalid.RootElement));
    }

    [Fact]
    public async Task UnknownPluginSettingsNamespace_FailsBeforeCreatingTheFrontend()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-plugin-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            bool frontendCreated = false;
            await using BindingHost host = BindingGenerator.CreateBuilder().UseDetachedFrontend(() => { frontendCreated = true; return new EmptyFrontend(); }).Build();
            GenerationResult result = await host.GenerateAsync(new(new()
            {
                schemaVersion = 2, library = new() { id = "settings", nativeName = "settings" }, source = new() { headers = ["unused.h"] },
                extensions = new() { options = ImmutableDictionary<string, PluginSettingsConfiguration>.Empty.Add("missing-plugin", new()) }
            }, root));
            Assert.False(result.success);
            Assert.False(frontendCreated);
            Assert.Contains("no registered plugin", result.GetDiagnosticSummary());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FinalValidators_ReadExactArtifactsAndBlockReplacingOutputOnFailure(bool releaseFailure)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-validator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "unused.h"), "/* Explicit empty neutral frontend fixture input. */");
        BindingConfiguration configuration = new()
        {
            schemaVersion = 2, library = new() { id = "gate", nativeName = "gate" }, source = new() { headers = ["unused.h"] },
            targets = [new() { id = "host" }], cache = new() { enabled = false }
        };
        List<string> trace = [];
        try
        {
            await using (BindingHost initial = PluginFrontendBuilder().Build())
                Assert.True((await initial.GenerateAsync(new(configuration, root))).success);
            string before = File.ReadAllText(Path.Combine(root, "Generated", "generation.report.json"));
            await using BindingHost host = PluginFrontendBuilder()
                .RegisterFactory<IGenerationValidator>(new("first", ExtensionPort.GenerationValidator, 10), _ => new ProbeValidator("first", 10, trace))
                .RegisterFactory<IGenerationValidator>(new("second", ExtensionPort.GenerationValidator, 0), _ => new ProbeValidator("second", 0, trace, reject: !releaseFailure, releaseFailure: releaseFailure)).Build();
            GenerationResult result = await host.GenerateAsync(new(configuration, root));
            Assert.False(result.success);
            Assert.Equal(new[] { "first", "second", "dispose:second", "dispose:first" }, trace);
            Assert.Equal(before, File.ReadAllText(Path.Combine(root, "Generated", "generation.report.json")));
            Assert.Empty(Directory.GetDirectories(root, ".bgcs-staging-*"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ConfigurationChangedByALateValidator_IsRejectedBeforePublication()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-config-final-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "unused.h"), "/* Explicit empty neutral frontend fixture input. */");
        string entry = Path.Combine(root, "bindgen.json");
        File.WriteAllText(entry, "{\"schemaVersion\":2,\"library\":{\"id\":\"gate\",\"nativeName\":\"gate\"},\"source\":{\"headers\":[\"unused.h\"]},\"targets\":[{\"id\":\"host\"}]}");
        try
        {
            GenerationRequest request = TestInputs.LoadRequest(entry);
            await using BindingHost host = PluginFrontendBuilder()
                .RegisterFactory<IGenerationValidator>(new("mutate", ExtensionPort.GenerationValidator, 0), _ => new ProbeValidator("mutate", 0, [], action: () => File.AppendAllText(entry, "\n"))).Build();
            GenerationResult result = await host.GenerateAsync(request);
            Assert.False(result.success);
            Assert.Contains("changed during generation", result.GetDiagnosticSummary());
            Assert.False(Directory.Exists(result.outputRoot));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class ProbeValidator(string identity, int order, List<string> trace, bool reject = false, bool releaseFailure = false, Action? action = null) : IGenerationValidator, IDisposable
    {
        public string id => identity;
        public int priority => order;
        public async ValueTask<ImmutableArray<BindingDiagnostic>> ValidateAsync(ValidationRequest request, CancellationToken cancellationToken)
        {
            trace.Add(identity);
            Assert.Equal(request.target.targetId.value, request.interop.targetId);
            string abi = await request.content.ReadTextAsync(request.artifacts.Single(artifact => artifact.relativePath.EndsWith("abi.manifest.json", StringComparison.Ordinal)).relativePath, cancellationToken);
            Assert.Contains(request.interop.targetId, abi);
            await Assert.ThrowsAsync<ArgumentException>(() => request.content.ReadTextAsync("undeclared", cancellationToken).AsTask());
            action?.Invoke();
            return reject ? [new(BindingDiagnosticSeverity.Error, "The final external gate rejected this candidate.", "FIXTURE-GATE")] : [];
        }
        public void Dispose() { trace.Add("dispose:" + identity); if (releaseFailure) throw new IOException("validator release failure"); }
    }

    private static AnalysisRequest Request() => AnalysisRequest.FromFiles(["native.h"]) with { targetId = "host" };

    private static BindingHostBuilder PluginFrontendBuilder() => BindingHost.CreateBuilder()
        .RegisterFactory<IHostServicesProvider>(new("fixture.host", ExtensionPort.HostServicesProvider), _ => new HostServicesProvider())
        .AddPlugin<DesktopTargetsExtension>()
        .AddPlugin<DotNetInteropPlugin>()
        .RegisterFactory<IManagedRuntimeProvider>(new("fixture.empty.runtime", ExtensionPort.ManagedRuntimeProvider), _ => new EmptyModelRuntime())
        .AddPlugin<FrontendPlugin>();

    private sealed class EmptyModelRuntime : IManagedRuntimeProvider
    {
        public bool TryResolve(ManagedRuntimeRequest request, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ManagedRuntimeProfile? profile)
        {
            profile = null;
            if (request.runtimeId.value != "fixture.empty-model") return false;
            if (request.target.byteOrder == NativeByteOrder.Unknown) throw new NotSupportedException("The fixture requires an explicit byte order.");
            profile = new(request.target.targetId.value + "/fixture.empty-model", request.runtimeId,
                NativeModuleMechanism.ExternalFunctionTable, [BindingImportMode.FunctionTable], false, false)
            {
                nativeTargetId = request.target.targetId.value,
                nativeTriple = request.target.triple,
                pointerSize = 4,
                byteOrder = request.target.byteOrder,
                callAbi = new("fixture.no-native-calls")
            };
            return true;
        }
    }

    private sealed class ProbePlugin(string? failure = null, int major = BGCS.Core.Versioning.ContractVersion.C_MAJOR, bool throwOnDispose = false) : IBindingPlugin
    {
        public ProbePlugin() : this(null) { }
        public List<string> trace { get; } = [];
        public bool disposed { get; private set; }
        public IExtensionRegistryBuilder? retainedRegistry { get; private set; }
        private bool m_changed;
        public PluginDescriptor descriptor => new("probe", m_changed ? "2.0.0" : "1.0.0", [new("probe.transform", ExtensionPort.NativeModuleTransform, 4)]) { contractsMajor = major };
        public void Configure(IExtensionRegistryBuilder registry, PluginConfiguration configuration)
        {
            trace.Add("configure");
            retainedRegistry = registry;
            if (failure == "missing") return;
            if (failure == "wrong-port") { registry.RegisterFactory<IFrontend>("probe.transform", _ => new EmptyFrontend()); return; }
            registry.RegisterFactory<INativeModuleTransform>(failure == "undeclared" ? "another" : "probe.transform", _ => { trace.Add("create"); return new ProbeTransform(trace); });
            if (failure == "duplicate") registry.RegisterFactory<INativeModuleTransform>("probe.transform", _ => new ProbeTransform(trace));
            m_changed = failure == "changing";
        }
        public void Dispose() { disposed = true; trace.Add("plugin.dispose"); if (throwOnDispose) throw new IOException("plugin cleanup failure"); }
    }

    private sealed class ProbeTransform(List<string> trace) : INativeModuleTransform, IDisposable
    {
        public string id => "probe.transform";
        public int priority => 4;
        public NativeModule Transform(NativeModule module, AnalysisContext context, CancellationToken cancellationToken) { trace.Add("transform"); return module; }
        public void Dispose() => trace.Add("service.dispose");
    }

    private sealed class EmptyFrontend : IFrontend
    {
        public ValueTask<FrontendPreparation> PrepareAsync(FrontendRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new FrontendPreparation(request, new([], []), []));
        }

        public ValueTask<FrontendResult> ParseAsync(FrontendRequest request, CancellationToken cancellationToken)
            => ValueTask.FromResult(new FrontendResult(new(request.target.targetId.value, request.target.triple,
                request.target.architectureId == "wasm32" ? 4 : TestComposition.PointerSize(request.target), [], [], [])
                { language = request.language, byteOrder = request.target.byteOrder }, new([], [])));
    }

    private sealed class SettingsPlugin : IBindingPlugin
    {
        public SettingsPlugin() { }
        public PluginConfiguration? configuration { get; private set; }
        public PluginDescriptor descriptor => new("settings.plugin", "1.0.0", [new("probe.transform", ExtensionPort.NativeModuleTransform, 4)])
        {
            configurationSchema = JsonSerializer.SerializeToElement(new
            {
                type = "object", additionalProperties = false,
                properties = new { enabled = new { type = "boolean" }, count = new { type = "integer", minimum = 0, maximum = 10 } },
                required = new[] { "enabled", "count" }
            })
        };
        public void Configure(IExtensionRegistryBuilder registry, PluginConfiguration configuration)
        {
            this.configuration = configuration;
            registry.RegisterFactory<INativeModuleTransform>("probe.transform", _ => new ProbeTransform([]));
        }
    }

    private sealed class FrontendPlugin : IBindingPlugin
    {
        public FrontendPlugin() { }
        public PluginDescriptor descriptor => new("probe.frontend-plugin", "1.0.0", [new("probe.frontend", ExtensionPort.Frontend)]);
        public void Configure(IExtensionRegistryBuilder registry, PluginConfiguration configuration) => registry.RegisterFactory<IFrontend>("probe.frontend", _ => new EmptyFrontend());
    }

    private sealed class TargetPlugin : IBindingPlugin
    {
        public TargetPlugin() { }
        public PluginDescriptor descriptor => new("probe.target-plugin", "1.0.0", [new("probe.target", ExtensionPort.NativeTargetProvider)]);
        public void Configure(IExtensionRegistryBuilder registry, PluginConfiguration configuration) => registry.RegisterFactory<INativeTargetProvider>("probe.target", _ => new CustomTarget(() => { }));
    }

    private sealed class CustomTarget(Action dispose, NativeByteOrder byteOrder = NativeByteOrder.LittleEndian) : INativeTargetProvider, IDisposable
    {
        public bool TryResolve(NativeTargetRequest request, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out NativeTargetDescriptor? target)
        {
            target = request.targetId.value == "custom-wasm32" ? new(request.targetId, "emscripten", "wasm32", "emscripten", "wasm32-unknown-emscripten", request.toolchain, byteOrder: byteOrder) : null;
            return target is not null;
        }
        public void Dispose() => dispose();
    }
}
