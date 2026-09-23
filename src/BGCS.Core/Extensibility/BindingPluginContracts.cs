using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;

namespace BGCS.Core.Extensibility;

/// <summary>Load-time revision of the current third-party plugin service contract.</summary>
public static class BindingPluginContract
{
    public const int CurrentVersion = 1;
}

/// <summary>Entry point implemented by a third-party BindGen-CS plugin.</summary>
public interface IBindingPlugin
{
    string Id { get; }
    string Version { get; }
    int ContractVersion { get; }
    void Configure(IBindingPluginHost host);
}

/// <summary>Host used by plugins to publish typed services without depending on generator internals.</summary>
public interface IBindingPluginHost
{
    int ContractVersion { get; }
    void Register<TService>(string id, TService service, int priority = 0) where TService : class;
}

/// <summary>Optional contract for extensions whose mutable state affects incremental generation.</summary>
public interface ICacheFingerprintProvider
{
    string GetCacheFingerprint();
}

/// <summary>One immutable service registration exposed for diagnostics and deterministic resolution.</summary>
public sealed record BindingPluginService<TService>(string Id, int Priority, TService Service) where TService : class;

/// <summary>Thread-safe, deterministic registry shared by plugin loaders and generator frontends.</summary>
public sealed class BindingPluginRegistry : IBindingPluginHost, ICacheFingerprintProvider
{
    private readonly object gate = new();
    private readonly Dictionary<Type, List<ServiceRegistration>> registrations = [];
    private readonly HashSet<string> pluginIds = new(StringComparer.Ordinal);
    private readonly List<string> pluginFingerprints = [];

    public int ContractVersion => BindingPluginContract.CurrentVersion;

    public int Count
    {
        get { lock (gate) return registrations.Values.Sum(values => values.Count); }
    }

    public bool HasPlugins
    {
        get { lock (gate) return pluginIds.Count > 0; }
    }

    public string GetCacheFingerprint()
    {
        lock (gate)
            return string.Join("\n", pluginFingerprints.OrderBy(value => value, StringComparer.Ordinal));
    }

    public void Register<TService>(string id, TService service, int priority = 0) where TService : class
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Plugin service IDs cannot be empty.", nameof(id));
        ArgumentNullException.ThrowIfNull(service);
        lock (gate)
        {
            if (!registrations.TryGetValue(typeof(TService), out List<ServiceRegistration>? services))
                registrations.Add(typeof(TService), services = []);
            if (services.Any(value => string.Equals(value.Id, id, StringComparison.Ordinal)))
                throw new InvalidOperationException($"A plugin service '{id}' is already registered for {typeof(TService).FullName}.");
            services.Add(new(id, priority, service));
            services.Sort(CompareRegistrations);
        }
    }

    public IReadOnlyList<BindingPluginService<TService>> GetServices<TService>() where TService : class
    {
        lock (gate)
        {
            return registrations.TryGetValue(typeof(TService), out List<ServiceRegistration>? services)
                ? services.Select(value => new BindingPluginService<TService>(value.Id, value.Priority, (TService)value.Service)).ToArray()
                : [];
        }
    }

    internal void CommitPlugins(IReadOnlyCollection<IBindingPlugin> plugins, string assemblyHash, BindingPluginRegistry staged)
    {
        string[] ids = plugins.Select(plugin => plugin.Id).ToArray();
        lock (gate)
        {
            string? duplicatePlugin = ids.FirstOrDefault(pluginIds.Contains);
            if (duplicatePlugin != null)
                throw new InvalidOperationException($"A BindGen-CS plugin with ID '{duplicatePlugin}' is already loaded.");
            foreach ((Type serviceType, List<ServiceRegistration> services) in staged.registrations)
            {
                if (registrations.TryGetValue(serviceType, out List<ServiceRegistration>? existing))
                {
                    string? duplicateService = services.Select(service => service.Id)
                        .FirstOrDefault(id => existing.Any(service => string.Equals(service.Id, id, StringComparison.Ordinal)));
                    if (duplicateService != null)
                        throw new InvalidOperationException($"A plugin service '{duplicateService}' is already registered for {serviceType.FullName}.");
                }
            }

            pluginIds.UnionWith(ids);
            pluginFingerprints.AddRange(plugins.Select(plugin => string.Join("|",
                plugin.Id,
                plugin.Version,
                plugin.ContractVersion,
                assemblyHash,
                plugin is ICacheFingerprintProvider provider ? provider.GetCacheFingerprint() : string.Empty)));
            pluginFingerprints.AddRange(staged.registrations
                .SelectMany(pair => pair.Value.Select(service => (ServiceType: pair.Key, Registration: service)))
                .Where(value => value.Registration.Service is ICacheFingerprintProvider)
                .OrderBy(value => value.ServiceType.FullName, StringComparer.Ordinal)
                .ThenBy(value => value.Registration.Id, StringComparer.Ordinal)
                .Select(value => string.Join("|",
                    "service",
                    value.ServiceType.AssemblyQualifiedName,
                    value.Registration.Id,
                    value.Registration.Priority,
                    ((ICacheFingerprintProvider)value.Registration.Service).GetCacheFingerprint())));
            foreach ((Type serviceType, List<ServiceRegistration> services) in staged.registrations)
            {
                if (!registrations.TryGetValue(serviceType, out List<ServiceRegistration>? existing))
                    registrations.Add(serviceType, existing = []);
                existing.AddRange(services);
                existing.Sort(CompareRegistrations);
            }
        }
    }

    private static int CompareRegistrations(ServiceRegistration left, ServiceRegistration right)
    {
        int priorityOrder = right.Priority.CompareTo(left.Priority);
        return priorityOrder != 0 ? priorityOrder : StringComparer.Ordinal.Compare(left.Id, right.Id);
    }

    private sealed record ServiceRegistration(string Id, int Priority, object Service);
}

/// <summary>Loads explicit plugin assemblies and activates public parameterless plugin entry points.</summary>
public static class BindingPluginLoader
{
    private static readonly ConcurrentDictionary<string, Assembly> LoadedAssemblies = new(StringComparer.Ordinal);

    public static IReadOnlyList<IBindingPlugin> Load(string assemblyPath, BindingPluginRegistry registry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        ArgumentNullException.ThrowIfNull(registry);
        string fullPath = Path.GetFullPath(assemblyPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"BindGen-CS plugin assembly not found: {fullPath}", fullPath);
        string assemblyHash;
        using (FileStream stream = File.OpenRead(fullPath))
            assemblyHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        string assemblyIdentity = string.Concat(fullPath, "\0", assemblyHash);
        Assembly assembly = LoadedAssemblies.GetOrAdd(assemblyIdentity, _ =>
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
            string details = string.Join(Environment.NewLine, exception.LoaderExceptions
                .Where(loaderException => loaderException != null)
                .Select(loaderException => loaderException!.Message));
            throw new InvalidOperationException($"Unable to inspect BindGen-CS plugin assembly '{fullPath}':{Environment.NewLine}{details}", exception);
        }
        List<IBindingPlugin> plugins = assemblyTypes.Where(type =>
                     typeof(IBindingPlugin).IsAssignableFrom(type) && !type.IsAbstract && type.IsClass && type.IsPublic)
                 .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(type => (IBindingPlugin)(Activator.CreateInstance(type)
                ?? throw new InvalidOperationException($"Unable to construct plugin entry point '{type.FullName}'.")))
            .ToList();
        foreach (IBindingPlugin plugin in plugins)
        {
            if (plugin.ContractVersion != BindingPluginContract.CurrentVersion)
                throw new InvalidOperationException(
                    $"Plugin '{plugin.Id}' requires contract {plugin.ContractVersion}; this host provides {BindingPluginContract.CurrentVersion}.");
            if (string.IsNullOrWhiteSpace(plugin.Id) || string.IsNullOrWhiteSpace(plugin.Version))
                throw new InvalidOperationException($"Plugin entry point '{plugin.GetType().FullName}' must provide a non-empty ID and version.");
        }
        if (plugins.Count == 0)
            throw new InvalidOperationException($"Assembly '{fullPath}' contains no public {nameof(IBindingPlugin)} implementation.");
        string? duplicateId = plugins.GroupBy(plugin => plugin.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
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
        private readonly AssemblyDependencyResolver resolver;

        public PluginLoadContext(string pluginPath, string assemblyHash)
            : base($"BGCS.Plugin:{Path.GetFileNameWithoutExtension(pluginPath)}:{assemblyHash[..12]}", isCollectible: false)
        {
            resolver = new(pluginPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            Assembly? shared = Default.Assemblies.FirstOrDefault(assembly =>
                AssemblyName.ReferenceMatchesDefinition(assembly.GetName(), assemblyName));
            if (shared != null)
                return shared;
            string? path = resolver.ResolveAssemblyToPath(assemblyName);
            return path == null ? null : LoadFromAssemblyPath(path);
        }

        protected override nint LoadUnmanagedDll(string unmanagedDllName)
        {
            string? path = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path == null ? nint.Zero : LoadUnmanagedDllFromPath(path);
        }
    }
}
