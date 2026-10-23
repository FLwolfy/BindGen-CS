using BGCS.Core.Hosting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using BGCS.Core.Common;
using BGCS.Core.Plugins;
using BGCS.Infrastructure.IO;
using System.Text.Json;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Inputs;

namespace BGCS.Infrastructure.Plugins;

/// <summary>Activates one neutral plugin in a collectible context that shares only the host Contracts identity.</summary>
public sealed class PluginLoader
{
    private readonly IHostPathService m_paths;
    /// <summary>Captures the explicitly selected host path identity service.</summary>
    /// <param name="paths">The host path service.</param>
    public PluginLoader(IHostPathService paths) => m_paths = paths ?? throw new ArgumentNullException(nameof(paths));
    /// <summary>Loads a verified explicit entry without sharing other application dependencies.</summary>
    /// <param name="manifest">The immutable local input closure.</param>
    /// <returns>A session-owned plugin that releases its load context after plugin disposal.</returns>
    public IBindingPlugin Activate(LockedPluginManifest manifest)
        => ActivateCore(manifest, worker: false, []);

    /// <summary>Activates an explicitly worker-only plugin while sharing pinned parser contract assemblies supplied by that worker.</summary>
    /// <param name="manifest">The complete locked worker entry and private asset closure.</param>
    /// <param name="sharedAssemblies">Exact worker-owned parser assemblies whose type identities must be shared.</param>
    /// <returns>A process-local owned plugin; this method is not a security sandbox.</returns>
    public IBindingPlugin ActivateWorker(
        LockedPluginManifest manifest,
        ImmutableArray<Assembly> sharedAssemblies
    )
        => ActivateCore(manifest, worker: true, sharedAssemblies);

    private IBindingPlugin ActivateCore(
        LockedPluginManifest manifest,
        bool worker,
        ImmutableArray<Assembly> sharedAssemblies
    ) {
        ArgumentNullException.ThrowIfNull(manifest);
        new PluginManifestReader(m_paths).Verify(manifest);
        if (manifest.manifest.workerOnly != worker) throw new InvalidOperationException(worker
            ? "Worker activation requires an explicitly worker-only manifest." : "A worker-only plugin cannot be activated in the SDK host.");
        if (sharedAssemblies.IsDefault) throw new ArgumentException("Shared worker assembly identities must be initialized.", nameof(sharedAssemblies));
        PluginLoadContext context = new(manifest, sharedAssemblies, m_paths);
        IBindingPlugin? plugin = null;
        try
        {
            Assembly entry = context.LoadEntry();
            Type type = entry.GetType(manifest.manifest.entryType, throwOnError: true)!;
            if (!type.IsPublic || type.IsAbstract || !type.IsClass || !typeof(IBindingPlugin).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) is null)
                throw new InvalidOperationException("The manifest entry must be a public concrete neutral plugin with a public parameterless constructor.");
            plugin = (IBindingPlugin)Activator.CreateInstance(type)!;
            PluginDescriptor actual = plugin.descriptor;
            PluginDescriptor expected = manifest.manifest.plugin;
            if (actual.id != expected.id || actual.version != expected.version || actual.contractsMajor != expected.contractsMajor
                || actual.minimumApiMinor != expected.minimumApiMinor || !actual.services.SequenceEqual(expected.services)
                || !JsonElement.DeepEquals(actual.configurationSchema, expected.configurationSchema))
                throw new InvalidOperationException("The plugin entry changed its locked manifest descriptor.");
            return plugin is ICacheFingerprintProvider ? new FingerprintPluginInstance(plugin, context) : new PluginInstance(plugin, context);
        }
        catch
        {
            try { plugin?.Dispose(); }
            finally { context.Unload(); }
            throw;
        }
    }

    private class PluginInstance(
        IBindingPlugin plugin,
        PluginLoadContext context
    ) : IBindingPlugin, IExternalInputProvider, IAsyncDisposable
    {
        private IBindingPlugin? m_plugin = plugin;
        private PluginLoadContext? m_context = context;
        protected IBindingPlugin implementation => m_plugin ?? throw new ObjectDisposedException(nameof(PluginInstance));
        public PluginDescriptor descriptor => implementation.descriptor;
        public void Configure(
            IExtensionRegistryBuilder registry,
            PluginConfiguration configuration
        ) => implementation.Configure(registry, configuration);
        public ImmutableArray<DeclaredExternalInput> GetExternalInputs(
            ExternalInputContext input,
            CancellationToken token
        )
            => implementation is IExternalInputProvider provider ? provider.GetExternalInputs(input, token) : [];
        public void Dispose()
        {
            IBindingPlugin? owned = Interlocked.Exchange(ref m_plugin, null);
            if (owned is null) return;
            PluginLoadContext? load = Interlocked.Exchange(ref m_context, null);
            try { owned?.Dispose(); }
            finally { load?.Unload(); }
        }
        public async ValueTask DisposeAsync()
        {
            IBindingPlugin? owned = Interlocked.Exchange(ref m_plugin, null);
            if (owned is null) return;
            PluginLoadContext? load = Interlocked.Exchange(ref m_context, null);
            try
            {
                if (owned is IAsyncDisposable asynchronous) await asynchronous.DisposeAsync().ConfigureAwait(false);
                else owned.Dispose();
            }
            finally { load?.Unload(); }
        }
    }

    private sealed class FingerprintPluginInstance(
        IBindingPlugin plugin,
        PluginLoadContext context
    )
        : PluginInstance(plugin, context), ICacheFingerprintProvider
    {
        public CacheFingerprintContribution CaptureFingerprint(
            CacheFingerprintContext input,
            CancellationToken token
        )
            => ((ICacheFingerprintProvider)implementation).CaptureFingerprint(input, token);
    }

    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly IHostPathService m_paths;
        private readonly LockedPluginManifest m_manifest;
        private readonly AssemblyDependencyResolver m_resolver;
        private readonly string m_entryPath;
        private readonly HashSet<string> m_framework;
        private readonly Dictionary<string, Assembly> m_shared = new(StringComparer.OrdinalIgnoreCase);

        internal PluginLoadContext(
            LockedPluginManifest manifest,
            ImmutableArray<Assembly> sharedAssemblies,
            IHostPathService paths
        ) : base("BGCS.Plugin:" + manifest.manifest.plugin.id + ":" + Guid.NewGuid().ToString("N"), isCollectible: true)
        {
            m_paths = paths;
            m_manifest = manifest;
            m_entryPath = PluginManifestReader.ResolveAsset(Path.GetDirectoryName(manifest.path)!, manifest.manifest.assembly, paths);
            foreach (Assembly assembly in sharedAssemblies)
            {
                ArgumentNullException.ThrowIfNull(assembly);
                string name = assembly.GetName().Name ?? throw new ArgumentException("A shared worker assembly requires an identity.");
                if (name == typeof(IBindingPlugin).Assembly.GetName().Name || !m_shared.TryAdd(name, assembly))
                    throw new ArgumentException("Shared parser assemblies cannot duplicate Contracts or another shared identity.");
            }
            m_resolver = new(m_entryPath);
            string framework = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            string[] platform = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
                ?? throw new PlatformNotSupportedException("The plugin loader requires an explicit framework assembly directory.")).Split(Path.PathSeparator);
            m_framework = platform.Where(path => paths.Equals(Path.GetDirectoryName(path)!, framework))
                .Select(path => Path.GetFileNameWithoutExtension(path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        internal Assembly LoadEntry() => LoadLockedAssembly(m_entryPath);

        protected override Assembly? Load(AssemblyName name)
        {
            Assembly contracts = typeof(IBindingPlugin).Assembly;
            if (string.Equals(name.Name, contracts.GetName().Name, StringComparison.OrdinalIgnoreCase))
            {
                if (name.Version?.Major != contracts.GetName().Version?.Major) throw new FileLoadException("The plugin references an incompatible Contracts assembly major.");
                return contracts;
            }
            if (name.Name is not null && m_shared.TryGetValue(name.Name, out Assembly? shared))
            {
                if (!string.Equals(name.FullName, shared.GetName().FullName, StringComparison.OrdinalIgnoreCase))
                    throw new FileLoadException("The worker plugin references an incompatible pinned parser assembly identity.");
                return shared;
            }
            if (name.Name is not null && m_framework.Contains(name.Name)) return null;
            string? path = m_resolver.ResolveAssemblyToPath(name);
            path ??= m_manifest.inputs.Where(input => input.path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                .Select(input => input.path).FirstOrDefault(candidate => string.Equals(Path.GetFileNameWithoutExtension(candidate), name.Name, StringComparison.OrdinalIgnoreCase));
            if (path is null) throw new FileNotFoundException("A plugin dependency is absent from its declared private closure.", name.Name);
            return LoadLockedAssembly(path);
        }

        protected override nint LoadUnmanagedDll(string name)
        {
            string? path = m_resolver.ResolveUnmanagedDllToPath(name);

            if (path is null) throw new DllNotFoundException("A native plugin dependency must resolve from its declared private asset closure.");
            _ = ReadLocked(path);
            return LoadUnmanagedDllFromPath(path);
        }

        private Assembly LoadLockedAssembly(string path)
        {
            using MemoryStream stream = new(ReadLocked(path), writable: false);
            return LoadFromStream(stream);
        }

        private byte[] ReadLocked(string path)
        {

            string absolute = Path.GetFullPath(path);
            string physical = PhysicalPathResolver.Resolve(absolute, m_paths);
            var expected = m_manifest.inputs.FirstOrDefault(input => m_paths.Equals(input.path, absolute))
                ?? m_manifest.inputs.FirstOrDefault(input => m_paths.Equals(input.resolvedPath, physical))
                ?? throw new FileLoadException("A plugin dependency resolved outside its declared private asset closure.", path);
            if (!m_paths.Equals(physical, expected.resolvedPath)
                || !m_paths.Equals(PhysicalPathResolver.Resolve(expected.path, m_paths), expected.resolvedPath))
                throw new FileLoadException("A plugin dependency changed its physical identity before loading.", path);
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.LongLength != expected.length || new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(bytes))) != expected.digest)
                throw new FileLoadException("A plugin dependency changed before its exact bytes could be loaded.", path);
            if (!m_paths.Equals(absolute, expected.path))
            {
                byte[] original = File.ReadAllBytes(expected.path);
                if (original.LongLength != expected.length || new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(original))) != expected.digest)
                    throw new FileLoadException("A locked logical plugin dependency changed while loading its canonical path.", expected.path);
            }
            if (!m_paths.Equals(PhysicalPathResolver.Resolve(absolute, m_paths), expected.resolvedPath)
                || !m_paths.Equals(PhysicalPathResolver.Resolve(expected.path, m_paths), expected.resolvedPath))
                throw new FileLoadException("A plugin dependency changed its physical identity while loading.", path);
            return bytes;
        }
    }
}
