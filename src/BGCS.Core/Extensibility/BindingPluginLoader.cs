using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;

namespace BGCS.Core.Extensibility;

/// <summary>
/// Loads explicit plugin assemblies into isolated dependency contexts and publishes validated services.
/// </summary>
public static class BindingPluginLoader
{
    private static readonly ConcurrentDictionary<string, Assembly> m_loadedAssemblies = new(StringComparer.Ordinal);

    /// <summary>
    /// Configures every public plugin entry point and publishes the complete candidate atomically.
    /// </summary>
    /// <param name="assemblyPath">
    /// The file containing public, concrete plugin classes with parameterless constructors.
    /// </param>
    /// <param name="registry">
    /// The destination registry; failed inspection or configuration leaves its snapshot unchanged.
    /// </param>
    /// <returns>
    /// The activated plugin instances in ordinal type-name order, owned by the generator host.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// The assembly path is empty or whitespace.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// The registry is null.
    /// </exception>
    /// <exception cref="FileNotFoundException">
    /// The assembly does not exist.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Entry points cannot be inspected, violate the service contract, or conflict with registrations.
    /// </exception>
    /// <remarks>
    /// Exceptions from plugin construction, configuration, or fingerprint capture propagate before publication.
    /// Loaded assemblies are retained for the generator process lifetime.
    /// </remarks>
    public static IReadOnlyList<IBindingPlugin> Load(
        string assemblyPath,
        BindingPluginRegistry registry
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        ArgumentNullException.ThrowIfNull(registry);
        string fullPath = Path.GetFullPath(assemblyPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"BindGen-CS plugin assembly not found: {fullPath}", fullPath);
        string assemblyHash;
        using (FileStream stream = File.OpenRead(fullPath))
            assemblyHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        string assemblyIdentity = string.Concat(fullPath, "\0", assemblyHash);
        Assembly assembly = m_loadedAssemblies.GetOrAdd(assemblyIdentity, _ =>
        {
            PluginLoadContext loadContext = new(fullPath, assemblyHash);
            return loadContext.LoadFromAssemblyPath(fullPath);
        });
        Type[] assemblyTypes;
        try
        {
            assemblyTypes = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            string details = string.Join(Environment.NewLine, exception.LoaderExceptions.Where(loaderException => loaderException != null).Select(loaderException => loaderException!.Message));
            throw new InvalidOperationException($"Unable to inspect BindGen-CS plugin assembly '{fullPath}':{Environment.NewLine}{details}", exception);
        }

        List<IBindingPlugin> plugins = assemblyTypes
            .Where(static type => typeof(IBindingPlugin).IsAssignableFrom(type) && !type.IsAbstract && type.IsClass && type.IsPublic)
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .Select(static type => (IBindingPlugin)(Activator.CreateInstance(type)
                ?? throw new InvalidOperationException($"Unable to construct plugin entry point '{type.FullName}'.")))
            .ToList();
        foreach (IBindingPlugin plugin in plugins)
        {
            if (plugin.contractVersion != BindingPluginContract.C_CURRENT_VERSION)
                throw new InvalidOperationException($"Plugin '{plugin.id}' requires contract {plugin.contractVersion}; this host provides {BindingPluginContract.C_CURRENT_VERSION}.");
            if (string.IsNullOrWhiteSpace(plugin.id) || string.IsNullOrWhiteSpace(plugin.version))
                throw new InvalidOperationException($"Plugin entry point '{plugin.GetType().FullName}' must provide a non-empty ID and version.");
        }

        if (plugins.Count == 0)
            throw new InvalidOperationException($"Assembly '{fullPath}' contains no public {nameof(IBindingPlugin)} implementation.");
        string? duplicateId = plugins.GroupBy(plugin => plugin.id, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicateId != null)
            throw new InvalidOperationException($"Assembly '{fullPath}' contains duplicate BindGen-CS plugin ID '{duplicateId}'.");
        BindingPluginRegistry staged = new();
        foreach (IBindingPlugin plugin in plugins)
            plugin.Configure(staged);
        registry.CommitPlugins(plugins, assemblyHash, staged);
        return plugins.AsReadOnly();
    }

    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver m_resolver;
        private readonly AssemblyLoadContext m_hostContext;

        public PluginLoadContext(
            string pluginPath,
            string assemblyHash
        ) : base($"BGCS.Plugin:{Path.GetFileNameWithoutExtension(pluginPath)}:{assemblyHash[..12]}", isCollectible: false)
        {
            m_resolver = new(pluginPath);
            m_hostContext = GetLoadContext(typeof(BindingPluginLoader).Assembly)
                ?? throw new InvalidOperationException("The generator host has no assembly load context.");
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // Contract identity belongs to the calling host, including SDK task hosts outside Default.
            Assembly? shared = m_hostContext.Assemblies.FirstOrDefault(assembly =>
                AssemblyName.ReferenceMatchesDefinition(assembly.GetName(), assemblyName));
            if (shared != null)
                return shared;
            string? path = m_resolver.ResolveAssemblyToPath(assemblyName);
            return path == null ? null : LoadFromAssemblyPath(path);
        }

        protected override nint LoadUnmanagedDll(string unmanagedDllName)
        {
            string? path = m_resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path == null ? nint.Zero : LoadUnmanagedDllFromPath(path);
        }
    }
}
