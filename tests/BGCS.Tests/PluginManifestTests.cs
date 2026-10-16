using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts.Common;
using BGCS.Contracts.Plugins;
using BGCS.Contracts.Ports;
using BGCS.Infrastructure.Plugins;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.Tests;

public sealed class PluginManifestTests : IDisposable
{
    private readonly string m_root = Path.Combine(Path.GetTempPath(), "bgcs-plugin-manifest-" + Guid.NewGuid().ToString("N"));
    public PluginManifestTests() => Directory.CreateDirectory(m_root);

    [Fact]
    public void Schema_ExportsStrictMetadataAndDigestRequirements()
    {
        var schema = new SchemaCatalog().CreatePluginSchema();
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
        Assert.Equal(3, schema["properties"]!["plugin"]!["properties"]!["contractsMajor"]!["const"]!.GetValue<int>());
        Assert.Equal("^[0-9a-f]{64}$", schema["properties"]!["assets"]!["items"]!["properties"]!["digest"]!["properties"]!["value"]!["pattern"]!.GetValue<string>());
    }

    [Fact]
    public void Loader_SharesContractsButKeepsOtherDependenciesPrivateAndReleasesItsContext()
    {
        string name = "BgcsPrivateDependency" + Guid.NewGuid().ToString("N");
        byte[] hostDependency = Compile(name, "public static class Dependency { public static string Value() => \"host\"; }");
        using (MemoryStream stream = new(hostDependency)) AssemblyLoadContext.Default.LoadFromStream(stream);
        byte[] privateDependency = Compile(name, "public static class Dependency { public static string Value() => \"private\"; }");
        File.WriteAllBytes(Path.Combine(m_root, name + ".dll"), privateDependency);
        byte[] entry = Compile("EntryPlugin", """
            using System;
            using System.Threading;
            using BGCS.Contracts.Native;
            using BGCS.Contracts.Requests;
            using BGCS.Contracts.Ports;
            using BGCS.Contracts.Plugins;
            public sealed class Entry : IBindingPlugin {
                public PluginDescriptor descriptor => new("dynamic.plugin", "1.0.0", [new("dynamic.transform", ExtensionPort.NativeModuleTransform)]);
                public void Configure(IExtensionRegistryBuilder registry, PluginConfiguration configuration) {
                    if (Dependency.Value() != "private") throw new InvalidOperationException("The host dependency leaked into the plugin.");
                    registry.RegisterFactory<INativeModuleTransform>("dynamic.transform", () => new Probe());
                }
                private sealed class Probe : INativeModuleTransform {
                    public string id => "dynamic.transform";
                    public int priority => 0;
                    public NativeModule Transform(NativeModule module, AnalysisContext context, CancellationToken token) => module;
                }
            }
            """, MetadataReference.CreateFromImage(privateDependency));
        File.WriteAllBytes(Path.Combine(m_root, "EntryPlugin.dll"), entry);
        PluginManifest manifest = new()
        {
            schemaVersion = 1, assembly = "EntryPlugin.dll", entryType = "Entry", plugin = new("dynamic.plugin", "1.0.0", [new("dynamic.transform", ExtensionPort.NativeModuleTransform)]),
            assets = [Asset("EntryPlugin.dll", entry), Asset(name + ".dll", privateDependency)]
        };
        LockedPluginManifest locked = Read(manifest);
        WeakReference context = RunIsolated(locked);
        for (int count = 0; context.IsAlive && count < 10; count++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        Assert.False(context.IsAlive);
    }

    [Theory]
    [InlineData("../outside.dll")]
    [InlineData("nested/../entry.dll")]
    [InlineData("nested\\entry.dll")]
    [InlineData("/absolute.dll")]
    [InlineData("C:/absolute.dll")]
    public void Manifest_RejectsEscapingAndNonportablePaths(string path)
    {
        byte[] entry = File.ReadAllBytes(typeof(NeutralPlugin.QuantityPlugin).Assembly.Location);
        PluginManifest manifest = QuantityManifest(entry) with { assets = [Asset(path, entry)] };
        Assert.Throws<InvalidDataException>(() => Read(manifest));
    }

    [Fact]
    public void Manifest_RejectsIncorrectHashDuplicateAssetsAndUntrackedDependencyMetadata()
    {
        byte[] entry = File.ReadAllBytes(typeof(NeutralPlugin.QuantityPlugin).Assembly.Location);
        File.WriteAllBytes(Path.Combine(m_root, "QuantityPlugin.dll"), entry);
        PluginManifest manifest = QuantityManifest(entry);
        Assert.Throws<InvalidDataException>(() => Read(manifest with { assets = [new("QuantityPlugin.dll", new(new string('0', 64)))] }));
        Assert.Throws<InvalidDataException>(() => Read(manifest with { assets = [manifest.assets[0], manifest.assets[0]] }));
        LockedPluginManifest locked = Read(manifest);
        File.WriteAllText(Path.Combine(m_root, "QuantityPlugin.deps.json"), "{}");
        Assert.Throws<IOException>(() => new PluginManifestReader().Verify(locked));
        Assert.Throws<InvalidDataException>(() => Read(manifest));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Loader_RejectsWorkerOnlyOrManifestDescriptorMismatch(bool workerOnly)
    {
        byte[] entry = File.ReadAllBytes(typeof(NeutralPlugin.QuantityPlugin).Assembly.Location);
        File.WriteAllBytes(Path.Combine(m_root, "QuantityPlugin.dll"), entry);
        PluginManifest manifest = QuantityManifest(entry);
        manifest = workerOnly ? manifest with { workerOnly = true } : manifest with { plugin = manifest.plugin with { version = "9.0.0" } };
        LockedPluginManifest locked = Read(manifest);
        Assert.Throws<InvalidOperationException>(() => new PluginLoader().Activate(locked));
    }

    [Fact]
    public async Task RequestLockedPluginMutation_IsRejectedBeforeFrontendOrPublication()
    {
        byte[] entry = File.ReadAllBytes(typeof(NeutralPlugin.QuantityPlugin).Assembly.Location);
        string assembly = Path.Combine(m_root, "QuantityPlugin.dll");
        File.WriteAllBytes(assembly, entry);
        _ = Read(QuantityManifest(entry));
        GenerationRequest request = new(new()
        {
            schemaVersion = 1, library = new() { id = "fixture", nativeName = "fixture" }, source = new() { headers = ["unused.h"] },
            extensions = new() { plugins = ["plugin.json"] }, cache = new() { enabled = false }
        }, m_root);
        Assert.Equal(2, request.pluginInputs.Length);
        File.AppendAllText(assembly, "changed");
        bool frontendCreated = false;
        await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithFrontendFactory(() => { frontendCreated = true; throw new InvalidOperationException(); }).Build();
        GenerationResult result = await host.GenerateAsync(request);
        Assert.False(result.success);
        Assert.False(frontendCreated);
        Assert.Contains("changed after discovery", result.GetDiagnosticSummary());
        Assert.False(Directory.Exists(result.outputRoot));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RunIsolated(LockedPluginManifest manifest)
    {
        using IBindingPlugin plugin = new PluginLoader().Activate(manifest);
        ProbeRegistry registry = new();
        plugin.Configure(registry, new(plugin.descriptor.id, JsonSerializer.SerializeToElement(ImmutableDictionary<string, string>.Empty), Path.GetDirectoryName(manifest.path)!));
        INativeModuleTransform service = Assert.IsAssignableFrom<INativeModuleTransform>(registry.factory!());
        Assert.Equal(typeof(INativeModuleTransform), service.GetType().GetInterface(typeof(INativeModuleTransform).FullName!));
        AssemblyLoadContext context = AssemblyLoadContext.GetLoadContext(service.GetType().Assembly)!;
        Assert.True(context.IsCollectible);
        Assert.NotSame(AssemblyLoadContext.Default, context);
        return new(context);
    }

    private sealed class ProbeRegistry : IExtensionRegistryBuilder
    {
        public Func<object>? factory { get; private set; }
        public void RegisterFactory<TService>(string id, Func<TService> factory) where TService : class => this.factory = () => factory();
    }

    private LockedPluginManifest Read(PluginManifest manifest)
    {
        string path = Path.Combine(m_root, "plugin.json");
        JsonSerializerOptions options = new() { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, options));
        return new PluginManifestReader().Read(path);
    }

    private static PluginManifest QuantityManifest(byte[] entry) => new()
    {
        schemaVersion = 1, assembly = "QuantityPlugin.dll", entryType = typeof(NeutralPlugin.QuantityPlugin).FullName!,
        plugin = new NeutralPlugin.QuantityPlugin().descriptor, assets = [Asset("QuantityPlugin.dll", entry)]
    };

    private static PluginAsset Asset(string path, byte[] bytes) => new(path, new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(bytes))));

    private static byte[] Compile(string name, string source, params MetadataReference[] additional)
    {
        string runtime = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        string[] platform = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        IEnumerable<MetadataReference> references = platform.Where(path => Path.GetDirectoryName(path) == runtime).Select(path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(IBindingPlugin).Assembly.Location)).Concat(additional);
        CSharpCompilation compilation = CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(source, new(LanguageVersion.CSharp14))], references,
            new(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        using MemoryStream output = new();
        var result = compilation.Emit(output);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return output.ToArray();
    }

    public void Dispose() => Directory.Delete(m_root, recursive: true);
}
