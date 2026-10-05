using System;
using System.Collections.Generic;
using System.Linq;

namespace BGCS.Core.Extensibility;

/// <summary>
/// Publishes deterministic service snapshots after complete plugin candidate validation.
/// </summary>
public sealed class BindingPluginRegistry : IBindingPluginHost, ICacheFingerprintProvider
{
    private readonly object m_gate = new();
    private Dictionary<Type, List<ServiceRegistration>> m_registrations = [];
    private HashSet<string> m_pluginIds = new(StringComparer.Ordinal);
    private string[] m_pluginFingerprints = [];

    /// <inheritdoc/>
    public int contractVersion => BindingPluginContract.C_CURRENT_VERSION;

    /// <summary>
    /// Gets the total number of typed service registrations in the current snapshot.
    /// </summary>
    public int count
    {
        get
        {
            lock (m_gate)
                return m_registrations.Values.Sum(static services => services.Count);
        }
    }

    /// <summary>
    /// Gets whether at least one explicitly loaded plugin has been published.
    /// </summary>
    public bool hasPlugins
    {
        get
        {
            lock (m_gate)
                return m_pluginIds.Count > 0;
        }
    }

    /// <inheritdoc/>
    public string GetCacheFingerprint()
    {
        lock (m_gate)
            return string.Join("\n", m_pluginFingerprints);
    }

    /// <inheritdoc/>
    public void Register<TService>(
        string id,
        TService service,
        int priority = 0
    ) where TService : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(service);
        lock (m_gate)
        {
            if (!m_registrations.TryGetValue(typeof(TService), out List<ServiceRegistration>? services))
                m_registrations.Add(typeof(TService), services = []);
            if (services.Any(registration => string.Equals(registration.id, id, StringComparison.Ordinal)))
                throw new InvalidOperationException($"A plugin service '{id}' is already registered for {typeof(TService).FullName}.");
            services.Add(new(id, priority, service));
            services.Sort(CompareRegistrations);
        }
    }

    /// <summary>
    /// Captures registrations for a contract in descending priority and ordinal identity order.
    /// </summary>
    /// <typeparam name="TService">
    /// The contract to discover.
    /// </typeparam>
    /// <returns>
    /// An isolated read-only snapshot, empty when the contract has no registrations.
    /// Services retain their plugin ownership.
    /// </returns>
    public IReadOnlyList<BindingPluginService<TService>> GetServices<TService>() where TService : class
    {
        lock (m_gate)
        {
            if (!m_registrations.TryGetValue(typeof(TService), out List<ServiceRegistration>? services))
                return Array.Empty<BindingPluginService<TService>>();
            return Array.AsReadOnly(services.Select(static registration =>
                new BindingPluginService<TService>(registration.id, registration.priority, (TService)registration.service)).ToArray());
        }
    }

    internal void CommitPlugins(
        IReadOnlyCollection<IBindingPlugin> plugins,
        string assemblyHash,
        BindingPluginRegistry staged
    ) {
        KeyValuePair<Type, ServiceRegistration[]>[] contributions;
        lock (staged.m_gate)
            contributions = staged.m_registrations.Select(static pair =>
                new KeyValuePair<Type, ServiceRegistration[]>(pair.Key, pair.Value.ToArray())).ToArray();

        // Extension callbacks must complete before publication and outside the live registry lock.
        string[] ids = plugins.Select(static plugin => plugin.id).ToArray();
        var fingerprints = new List<string>();
        foreach (IBindingPlugin plugin in plugins)
        {
            string state = plugin is ICacheFingerprintProvider provider ? provider.GetCacheFingerprint() : string.Empty;
            fingerprints.Add(string.Join("|", plugin.id, plugin.version, plugin.contractVersion, assemblyHash, state));
        }
        foreach ((Type serviceType, ServiceRegistration[] services) in contributions)
        {
            foreach (ServiceRegistration registration in services)
            {
                if (registration.service is ICacheFingerprintProvider provider)
                    fingerprints.Add(string.Join("|", "service", serviceType.AssemblyQualifiedName,
                        registration.id, registration.priority, provider.GetCacheFingerprint()));
            }
        }

        lock (m_gate)
        {
            var nextIds = new HashSet<string>(m_pluginIds, StringComparer.Ordinal);
            foreach (string id in ids)
            {
                if (!nextIds.Add(id))
                    throw new InvalidOperationException($"A BindGen-CS plugin with ID '{id}' is already loaded.");
            }
            var nextRegistrations = m_registrations.ToDictionary(static pair => pair.Key, static pair => new List<ServiceRegistration>(pair.Value));
            foreach ((Type serviceType, ServiceRegistration[] services) in contributions)
            {
                if (!nextRegistrations.TryGetValue(serviceType, out List<ServiceRegistration>? existing))
                    nextRegistrations.Add(serviceType, existing = []);
                foreach (ServiceRegistration registration in services)
                {
                    if (existing.Any(value => string.Equals(value.id, registration.id, StringComparison.Ordinal)))
                        throw new InvalidOperationException($"A plugin service '{registration.id}' is already registered for {serviceType.FullName}.");
                    existing.Add(registration);
                }
                existing.Sort(CompareRegistrations);
            }
            string[] nextFingerprints = m_pluginFingerprints.Concat(fingerprints)
                .OrderBy(static value => value, StringComparer.Ordinal).ToArray();
            m_registrations = nextRegistrations;
            m_pluginIds = nextIds;
            m_pluginFingerprints = nextFingerprints;
        }
    }

    private static int CompareRegistrations(
        ServiceRegistration left,
        ServiceRegistration right
    ) {
        int priorityOrder = right.priority.CompareTo(left.priority);
        return priorityOrder != 0 ? priorityOrder : StringComparer.Ordinal.Compare(left.id, right.id);
    }

    private sealed record ServiceRegistration(
        string id,
        int priority,
        object service
    );
}
