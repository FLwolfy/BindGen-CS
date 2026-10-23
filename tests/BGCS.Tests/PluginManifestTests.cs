using System.Threading;
using BGCS.Core.Versioning;
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
using BGCS.Core.Common;
using BGCS.Core.Plugins;
using BGCS.Core.Ports;
using BGCS.Infrastructure.Plugins;
using BGCS.Infrastructure.Execution;
using BGCS.Infrastructure.IO;
using System.Text;
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
        Assert.Equal(ContractVersion.C_MAJOR, schema["properties"]!["plugin"]!["properties"]!["contractsMajor"]!["const"]!.GetValue<int>());
        Assert.Equal("^[0-9a-f]{64}$", schema["properties"]!["assets"]!["items"]!["properties"]!["digest"]!["properties"]!["value"]!["pattern"]!.GetValue<string>());
    }

    [Fact]
    public void Loader_SharesContractsButKeepsOtherDependenciesPrivateAndReleasesItsContext()
    {
        WeakReference context = RunIsolated(PrivateDependencyManifest(m_root));
        for (int count = 0; context.IsAlive && count < 10; count++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        Assert.False(context.IsAlive);
    }

    [Fact]
    public async Task CanonicalResolverPathsKeepLogicalClosureIdentityAndRejectRetargetedOrUntrackedAssets()
    {
        string physical = Path.Combine(m_root, "physical"), logical = Path.Combine(m_root, "logical");
        Directory.CreateDirectory(physical);
        bool linked = false;
        try
        {
            await CreateAlias(logical, physical);
            linked = true;
            LockedPluginManifest locked = PrivateDependencyManifest(logical);
            LockedPluginManifest canonical = locked with { path = PhysicalPathResolver.Resolve(locked.path, TestHostPaths.Create()) };
            Assert.NotEqual(locked.path, canonical.path);
            Assert.All(locked.inputs, input => Assert.StartsWith(logical + Path.DirectorySeparatorChar, input.path, StringComparison.Ordinal));
            _ = RunIsolated(canonical);

            string untracked = Path.Combine(m_root, "untracked");
            Directory.CreateDirectory(untracked);
            foreach (string file in Directory.GetFiles(physical)) File.Copy(file, Path.Combine(untracked, Path.GetFileName(file)));
            FileLoadException outside = Assert.Throws<FileLoadException>(() => new PluginLoader(TestHostPaths.Create()).Activate(canonical with { path = Path.Combine(untracked, "plugin.json") }));
            Assert.Contains("outside its declared private asset closure", outside.Message, StringComparison.Ordinal);

            string dependency = Assert.Single(Directory.GetFiles(physical, "BgcsPrivateDependency*.dll"));
            byte[] original = File.ReadAllBytes(dependency), changed = original.ToArray();
            changed[^1] ^= 1;
            File.WriteAllBytes(dependency, changed);
            try { Assert.Throws<IOException>(() => new PluginLoader(TestHostPaths.Create()).Activate(canonical)); }
            finally { File.WriteAllBytes(dependency, original); }

            DeleteAlias(logical);
            linked = false;
            await CreateAlias(logical, untracked);
            linked = true;
            IOException retargeted = Assert.Throws<IOException>(() => new PluginLoader(TestHostPaths.Create()).Activate(canonical));
            Assert.Contains("redirected", retargeted.Message, StringComparison.Ordinal);
            Assert.Contains(logical, retargeted.Message, StringComparison.Ordinal);
            Assert.Equal(original, File.ReadAllBytes(Path.Combine(untracked, Path.GetFileName(dependency))));
        }
        finally { if (linked) DeleteAlias(logical); }
    }

    private static LockedPluginManifest PrivateDependencyManifest(string root)
    {
        string name = "BgcsPrivateDependency" + Guid.NewGuid().ToString("N");
        byte[] hostDependency = Compile(name, "public static class Dependency { public static string Value() => \"host\"; }");
        using (MemoryStream stream = new(hostDependency)) AssemblyLoadContext.Default.LoadFromStream(stream);
        byte[] privateDependency = Compile(name, "public static class Dependency { public static string Value() => \"private\"; }");
        File.WriteAllBytes(Path.Combine(root, name + ".dll"), privateDependency);
        byte[] entry = Compile("EntryPlugin", """
            using System;
            using System.Threading;
            using BGCS.Core.Native;
            using BGCS.Core.Requests;
            using BGCS.Core.Ports;
            using BGCS.Core.Plugins;
            public sealed class Entry : IBindingPlugin {
                public PluginDescriptor descriptor => new("dynamic.plugin", "1.0.0", [new("dynamic.transform", ExtensionPort.NativeModuleTransform)]);
                public void Configure(IExtensionRegistryBuilder registry, PluginConfiguration configuration) {
                    if (Dependency.Value() != "private") throw new InvalidOperationException("The host dependency leaked into the plugin.");
                    registry.RegisterFactory<INativeModuleTransform>("dynamic.transform", _ => new Probe());
                }
                private sealed class Probe : INativeModuleTransform {
                    public string id => "dynamic.transform";
                    public int priority => 0;
                    public NativeModule Transform(NativeModule module, AnalysisContext context, CancellationToken token) => module;
                }
            }
            """, MetadataReference.CreateFromImage(privateDependency));
        File.WriteAllBytes(Path.Combine(root, "EntryPlugin.dll"), entry);
        PluginManifest manifest = new()
        {
            schemaVersion = 1,
            assembly = "EntryPlugin.dll",
            entryType = "Entry",
            plugin = new("dynamic.plugin", "1.0.0", [new("dynamic.transform", ExtensionPort.NativeModuleTransform)]),
            assets = [Asset("EntryPlugin.dll", entry), Asset(name + ".dll", privateDependency)]
        };
        string path = Path.Combine(root, "plugin.json");
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } }));
        return new PluginManifestReader(TestHostPaths.Create()).Read(path);
    }

    private async Task CreateAlias(
        string path,
        string target
    ) {
        Assert.StartsWith(Path.GetFullPath(m_root) + Path.DirectorySeparatorChar, Path.GetFullPath(path), StringComparison.Ordinal);
        Assert.StartsWith(Path.GetFullPath(m_root) + Path.DirectorySeparatorChar, Path.GetFullPath(target), StringComparison.Ordinal);
        if (OperatingSystem.IsWindows())
        {
            string executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            string command = "$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path '" + path.Replace("'", "''", StringComparison.Ordinal)
                + "' -Target '" + target.Replace("'", "''", StringComparison.Ordinal) + "' | Out-Null";
            var result = await ProcessExecutor.ExecuteAsync(executable,
                ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(command))],
                m_root, TimeSpan.FromSeconds(30));
            Assert.True(result.exitCode == 0, result.standardOutput + Environment.NewLine + result.standardError);
            Assert.NotNull(new DirectoryInfo(path).LinkTarget);
        }
        else Directory.CreateSymbolicLink(path, target);
    }

    private static void DeleteAlias(string path) { if (OperatingSystem.IsWindows()) Directory.Delete(path); else File.Delete(path); }

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
        Assert.Throws<IOException>(() => new PluginManifestReader(TestHostPaths.Create()).Verify(locked));
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
        Assert.Throws<InvalidOperationException>(() => new PluginLoader(TestHostPaths.Create()).Activate(locked));
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
            schemaVersion = 2,
            library = new() { id = "fixture", nativeName = "fixture" },
            source = new() { headers = ["unused.h"] },
            extensions = new() { plugins = ["plugin.json"] },
            cache = new() { enabled = false }
        }, m_root);
        request = await request.MaterializeAsync(TestHostPaths.Create(), CancellationToken.None);
        Assert.Equal(2, request.pluginSnapshots.SelectMany(snapshot => snapshot.files).Count());
        File.AppendAllText(assembly, "changed");
        bool frontendCreated = false;
        await using BindingHost host = BindingGenerator.CreateBuilder().UseDetachedFrontend(() => { frontendCreated = true; throw new InvalidOperationException(); }).Build();
        GenerationResult result = await host.GenerateAsync(request);
        Assert.False(result.success);
        Assert.False(frontendCreated);
        Assert.Contains("changed after discovery", result.GetDiagnosticSummary());
        Assert.False(Directory.Exists(result.outputRoot));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RunIsolated(LockedPluginManifest manifest)
    {
        using IBindingPlugin plugin = new PluginLoader(TestHostPaths.Create()).Activate(manifest);
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
        public void RegisterFactory<TService>(
            string id,
            Func<ExtensionActivationContext, TService> factory
        ) where TService : class => this.factory = () => factory(new(null, AppContext.BaseDirectory));
    }

    private LockedPluginManifest Read(PluginManifest manifest)
    {
        string path = Path.Combine(m_root, "plugin.json");
        JsonSerializerOptions options = new() { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, options));
        return new PluginManifestReader(TestHostPaths.Create()).Read(path);
    }

    private static PluginManifest QuantityManifest(byte[] entry) => new()
    {
        schemaVersion = 1,
        assembly = "QuantityPlugin.dll",
        entryType = typeof(NeutralPlugin.QuantityPlugin).FullName!,
        plugin = new NeutralPlugin.QuantityPlugin().descriptor,
        assets = [Asset("QuantityPlugin.dll", entry)]
    };

    private static PluginAsset Asset(
        string path,
        byte[] bytes
    ) => new(path, new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(bytes))));

    private static byte[] Compile(
        string name,
        string source,
        params MetadataReference[] additional
    ) {
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
