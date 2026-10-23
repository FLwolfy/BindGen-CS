using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;
using BGCS.Core.Plugins;
using BGCS.Core.Ports;
using BGCS.Core.Requests;
using BGCS.Tests.NeutralPlugin;
using Xunit;

namespace BGCS.Tests;

public sealed class ManagedTransformTests
{
    [Fact]
    public async Task ContractsOnlyPluginComposesWithBuiltInProjectionAndRetainsNativeEnumValues()
    {
        using Scratch scratch = new();
        using ManagedNamesPlugin metadata = new();
        List<ManagedNamesPlugin> plugins = [];
        await using BindingHost host = Builder().AddPluginFactory(metadata.descriptor, () =>
        { ManagedNamesPlugin plugin = new(); plugins.Add(plugin); return plugin; }, pathBase: scratch.root).Build();
        GenerationResult result = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(Configuration(), scratch.root), compileManaged: true);
        Assert.True(result.success, result.GetDiagnosticSummary());
        Assert.NotEmpty(plugins);
        Assert.All(plugins, plugin =>
        {
            Assert.True(plugin.disposed);
            Assert.True(Assert.IsType<ManagedNamesTransform>(plugin.transform).disposed);
        });
        TargetInspection target = Assert.Single(result.targetModels);
        Assert.Same(target.interop, target.managed!.interop);
        var native = Assert.Single(target.interop.types, type => type.category == NativeTypeCategory.Enumeration);
        Assert.Equal(new[] { "7", "9" }, native.enumValues.Select(value => value.evaluatedValue));
        ManagedType managed = Assert.Single(target.managed.types, type => type.nativeType == native.id);
        Assert.Equal(new[] { "custom_first", "custom_second" }, managed.memberNames.Select(member => member.name));
        Assert.Contains("custom_first", ManagedSources(result), StringComparison.Ordinal);
        Assert.DoesNotContain(typeof(ManagedNamesPlugin).Assembly.GetReferencedAssemblies(), reference =>
            reference.Name?.StartsWith("BGCS", StringComparison.Ordinal) == true && reference.Name != "BGCS.Core");
    }

    [Fact]
    public async Task FingerprintedManagedStateChangesEmissionKeyWithoutChangingNativeFacts()
    {
        using Scratch scratch = new();
        string prefix = "first_";
        await using BindingHost host = Builder().RegisterFactory<IManagedApiTransform>(new("fixture.managed-names", ExtensionPort.ManagedApiTransform, 5), _ => new ManagedNamesTransform(prefix)).Build();
        GenerationRequest request = new(Configuration(), scratch.root);
        GenerationResult cold = await host.GenerateAsync(request);
        Assert.True(cold.success, cold.GetDiagnosticSummary());
        Assert.Equal("miss", EmissionCache(cold));
        GenerationResult warm = await host.GenerateAsync(request);
        Assert.True(warm.success, warm.GetDiagnosticSummary());
        Assert.Equal("hit", EmissionCache(warm));
        prefix = "next_";
        GenerationResult changed = await host.GenerateAsync(request);
        Assert.True(changed.success, changed.GetDiagnosticSummary());
        Assert.Equal("miss", EmissionCache(changed));
        Assert.Contains("next_first", ManagedSources(changed), StringComparison.Ordinal);
        Assert.DoesNotContain("first_first", ManagedSources(changed), StringComparison.Ordinal);
        Assert.Equal(JsonSerializer.Serialize(Assert.Single(cold.targetModels).interop.types.Single(type => type.category == NativeTypeCategory.Enumeration).enumValues),
            JsonSerializer.Serialize(Assert.Single(changed.targetModels).interop.types.Single(type => type.category == NativeTypeCategory.Enumeration).enumValues));
    }

    [Fact]
    public async Task StablePriorityAndIdOrderAndUnfingerprintedCacheRefusalAreSharedWithOtherPorts()
    {
        using Scratch scratch = new();
        List<string> trace = [];
        BindingHostBuilder builder = Builder().RegisterFactory<IManagedApiTransform>(new("z-last", ExtensionPort.ManagedApiTransform, 0), _ => new Observed("z-last", trace))
            .RegisterFactory<IManagedApiTransform>(new("b-first", ExtensionPort.ManagedApiTransform, 10), _ => new Observed("b-first", trace))
            .RegisterFactory<IManagedApiTransform>(new("a-first", ExtensionPort.ManagedApiTransform, 10), _ => new Observed("a-first", trace));
        Assert.Throws<InvalidOperationException>(() => builder.RegisterFactory<IInteropTransform>(new("a-first", ExtensionPort.InteropTransform, 0), _ => null!));
        Assert.Throws<InvalidOperationException>(() => builder.RegisterFactory<IManagedApiTransform>(new("a-first", ExtensionPort.ManagedApiTransform, 0), _ => null!));
        await using BindingHost host = builder.Build();
        GenerationResult result = await host.GenerateAsync(new(Configuration(), scratch.root));
        Assert.True(result.success, result.GetDiagnosticSummary());
        Assert.Equal("disabled", EmissionCache(result));
        Assert.Equal(new[] { "a-first", "b-first", "z-last", "dispose:z-last", "dispose:b-first", "dispose:a-first" }, trace);
    }

    [Theory]
    [InlineData("clone-abi")]
    [InlineData("change-abi")]
    [InlineData("null")]
    [InlineData("dispose")]
    public async Task InvalidManagedTransformationCannotReplaceTheLastCompletePublication(string fault)
    {
        using Scratch scratch = new();
        GenerationRequest request = new(Configuration(), scratch.root);
        await using (BindingHost original = Builder().Build())
            Assert.True((await original.GenerateAsync(request)).success);
        string output = Path.Combine(scratch.root, "Generated");
        Dictionary<string, byte[]> before = Directory.GetFiles(output, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(output, path), File.ReadAllBytes, StringComparer.Ordinal);
        Faulty service = new(fault);
        await using BindingHost changed = Builder().RegisterFactory<IManagedApiTransform>(new("faulty", ExtensionPort.ManagedApiTransform, 0), _ => service).Build();
        GenerationResult rejected = await changed.GenerateAsync(request);
        Assert.False(rejected.success);
        Assert.True(service.disposed);
        Assert.Contains(rejected.diagnostics, diagnostic => diagnostic.isFailure);
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), Directory.GetFiles(output, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(output, path)).Order(StringComparer.Ordinal));
        foreach ((string path, byte[] bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(output, path)));
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("priority")]
    public async Task FrozenRegistrationMetadataCannotReplaceTheLastCompletePublication(string mutation)
    {
        using Scratch scratch = new();
        GenerationRequest request = new(Configuration(), scratch.root);
        await using (BindingHost original = Builder().Build())
            Assert.True((await original.GenerateAsync(request)).success);
        string output = Path.Combine(scratch.root, "Generated");
        Dictionary<string, byte[]> before = Directory.GetFiles(output, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(output, path), File.ReadAllBytes, StringComparer.Ordinal);
        MetadataMutationPlugin plugin = new(mutation);
        await using BindingHost changed = Builder().AddPluginFactory(plugin.descriptor, () => plugin).Build();
        GenerationResult rejected = await changed.GenerateAsync(request);
        Assert.False(rejected.success);
        Assert.True(plugin.disposed);
        Assert.Contains(mutation == "identity" ? "frozen outside Configure" : "changed its frozen discovery descriptor", rejected.GetDiagnosticSummary());
        if (mutation == "identity") Assert.True(plugin.transformDisposed);
        else Assert.False(plugin.serviceCreated);
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), Directory.GetFiles(output, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(output, path)).Order(StringComparer.Ordinal));
        foreach ((string path, byte[] bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(output, path)));
    }

    private sealed class MetadataMutationPlugin(string mutation) : IBindingPlugin
    {
        private int m_priority;
        internal bool disposed { get; private set; }
        internal bool serviceCreated { get; private set; }
        internal bool transformDisposed { get; private set; }
        public PluginDescriptor descriptor => new("fixture.metadata-mutation", "1.0.0",
            [new("fixture.metadata-transform", ExtensionPort.ManagedApiTransform, m_priority)]);
        public void Configure(IExtensionRegistryBuilder registry, PluginConfiguration configuration)
        {
            registry.RegisterFactory<IManagedApiTransform>("fixture.metadata-transform", _ =>
            {
                serviceCreated = true;
                return new LateRegistrationTransform(() => registry.RegisterFactory<IManagedApiTransform>("late.identity", _ => throw new InvalidOperationException("Late service activated.")),
                    () => transformDisposed = true);
            });
            if (mutation == "priority") m_priority++;
        }
        public void Dispose() => disposed = true;
    }

    private sealed class LateRegistrationTransform(Action register, Action release) : IManagedApiTransform, IDisposable
    {
        public ManagedApiModel Transform(ManagedApiModel model, AnalysisContext context, CancellationToken cancellationToken)
        { register(); return model; }
        public void Dispose() => release();
    }

    private sealed class Observed(string name, List<string> trace) : IManagedApiTransform, IDisposable
    {
        public ManagedApiModel Transform(ManagedApiModel model, AnalysisContext context, CancellationToken cancellationToken)
        { trace.Add(name); return model; }
        public void Dispose() => trace.Add("dispose:" + name);
    }

    private sealed class Faulty(string fault) : IManagedApiTransform, IDisposable
    {
        internal bool disposed { get; private set; }
        public ManagedApiModel Transform(ManagedApiModel model, AnalysisContext context, CancellationToken cancellationToken)
        {
            if (fault == "null") return null!;
            if (fault is not ("clone-abi" or "change-abi")) return model;
            InteropModule abi = model.interop;
            InteropModule replacement = new(abi.libraryId, abi.targetId, abi.pointerSize, abi.types,
                abi.functions, abi.diagnostics) { byteOrder = abi.byteOrder };
            if (fault == "change-abi") replacement = new(abi.libraryId, abi.targetId, abi.pointerSize,
                abi.types.Select(type => type.category == NativeTypeCategory.Enumeration ? type with { size = 8 } : type).ToImmutableArray(), abi.functions, []);
            return model with { interop = replacement };
        }
        public void Dispose()
        {
            disposed = true;
            if (fault == "dispose") throw new InvalidOperationException("Managed transform cleanup failed.");
        }
    }

    private static string EmissionCache(GenerationResult result) => Assert.Single(result.cacheStages, stage => stage.stage == "csharpEmission").status;
    private static string ManagedSources(GenerationResult result) => string.Join("\n", result.artifacts.Where(artifact => artifact.mediaType == "text/x-csharp")
        .Select(artifact => File.ReadAllText(Path.Combine(result.outputRoot, artifact.relativePath))));
    private static BindingConfiguration Configuration() => new()
    {
        schemaVersion = 2, library = new() { id = "managed_transform", nativeName = "managed_transform" },
        source = new() { headers = ["native.h"], parseMacros = false },
        api = new() { @namespace = "Generated.ManagedTransform", name = "ManagedApi" }
    };
    private static BindingHostBuilder Builder()
    {
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "BindGen-CS.slnx"))) repository = repository.Parent;
        Assert.NotNull(repository);
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return BindingGenerator.CreateBuilder().UseWorkerDirectory(Path.Combine(repository.FullName, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0"));
    }
    private sealed class Scratch : IDisposable
    {
        internal string root { get; } = Path.Combine(Path.GetTempPath(), "bgcs-managed-transform-" + Guid.NewGuid().ToString("N"));
        internal Scratch()
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "native.h"), "enum Mode { FIRST = 7, SECOND = 9 }; int echo(enum Mode value);");
        }
        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
