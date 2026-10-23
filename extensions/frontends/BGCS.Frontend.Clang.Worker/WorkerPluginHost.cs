using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Common;
using BGCS.Core.Hosting;
using BGCS.Core.Inputs;
using BGCS.Core.Plugins;
using BGCS.Frontend.Clang.Advanced;
using BGCS.Infrastructure.Configuration;
using BGCS.Infrastructure.IO;
using BGCS.Infrastructure.Plugins;
using BGCS.Infrastructure.Workers;

namespace BGCS.Frontend.Clang.Worker;

internal sealed class WorkerPluginHost : IAsyncDisposable
{
    private readonly List<IBindingPlugin> m_plugins = [];
    private readonly List<Entry> m_entries = [];
    private readonly List<Service> m_services = [];
    private readonly List<SourceSnapshot> m_preconfigurationInputs = [];
    private readonly CacheFingerprintContext? m_context;
    private readonly HostServices m_host;
    internal IReadOnlyList<IClangFactContributor> contributors => m_services.Select(service => service.Instance).ToArray();

    private WorkerPluginHost(
        CacheFingerprintContext? context,
        HostServices host
    ) { m_context = context; m_host = host; }

    internal static async ValueTask<WorkerPluginHost> CreateAsync(
        WorkerParseRequest request,
        HostServices hostServices,
        CancellationToken token
    ) {
        if (request.pluginActivations.IsDefault) throw new InvalidDataException("Worker plugin activations must be initialized.");
        if (!request.pluginActivations.IsEmpty && request.pluginContext is null)
            throw new InvalidDataException("Worker plugins require their complete frozen fingerprint context.");
        if (!request.pluginActivations.IsEmpty && (request.pluginContext!.target.targetId != request.request.target.targetId
            || request.pluginContext.target.triple != request.request.target.triple))
            throw new InvalidDataException("Worker plugin discovery and parsing require the same resolved native target identity and triple.");
        WorkerPluginHost host = new(request.pluginContext, hostServices);
        HashSet<string> plugins = new(StringComparer.Ordinal), services = new(StringComparer.Ordinal);
        try
        {
            foreach (WorkerPluginActivation activation in request.pluginActivations.OrderBy(item => item.manifest.plugin.id, StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                if (activation.parserIdentity != ClangParserIdentity.C_IDENTITY || activation.manifest.workerParserIdentity != ClangParserIdentity.C_IDENTITY || !activation.manifest.workerOnly)
                    throw new InvalidOperationException("A worker plugin requires the exact pinned parser identity and an explicit worker-only manifest.");
                PluginDescriptor descriptor = activation.manifest.plugin;
                if (descriptor.contractsMajor != BGCS.Core.Versioning.ContractVersion.C_MAJOR
                    || descriptor.minimumApiMinor < 0 || descriptor.minimumApiMinor > BGCS.Core.Versioning.ContractVersion.C_API_MINOR
                    || !plugins.Add(descriptor.id) || descriptor.services.IsDefaultOrEmpty
                    || descriptor.services.Any(service => service.port != ExtensionPort.FrontendFactContributor || !services.Add(service.id)))
                    throw new InvalidOperationException("A worker plugin has incompatible contracts, duplicate identities or a non-worker service port.");
                LockedPluginManifest locked = new PluginManifestReader(hostServices.paths).Read(activation.manifestPath);
                if (!JsonElement.DeepEquals(JsonSerializer.SerializeToElement(locked.manifest, WorkerProtocol.jsonOptions), JsonSerializer.SerializeToElement(activation.manifest, WorkerProtocol.jsonOptions))
                    || !locked.inputs.SequenceEqual(activation.inputs) || !locked.missingInputs.SequenceEqual(activation.missingInputs))
                    throw new IOException("The worker activation no longer matches its locked manifest input closure.");
                PluginConfiguration configuration = new(descriptor.id, activation.settings, activation.pathBase, activation.valuePathBases);
                new ExtensionSchemaValidator().ValidateSchema(descriptor.configurationSchema);
                new ExtensionSchemaValidator().Validate(descriptor.configurationSchema, configuration.settings, descriptor.id);
                IBindingPlugin plugin = new PluginLoader(hostServices.paths).ActivateWorker(locked, ClangPluginAssemblyIdentity.sharedAssemblies);
                host.m_plugins.Add(plugin);
                string closure = new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(activation.inputs, WorkerProtocol.jsonOptions)))).value;
                Entry entry = new(locked, plugin, closure);
                host.m_entries.Add(entry);
                host.m_preconfigurationInputs.Add(host.CaptureDeclared(plugin, token));
                Registry registry = new(descriptor, new(hostServices, activation.pathBase));
                try { plugin.Configure(registry, configuration); registry.Complete(); }
                finally { registry.Seal(); }
                if (!JsonElement.DeepEquals(JsonSerializer.SerializeToElement(plugin.descriptor, WorkerProtocol.jsonOptions), JsonSerializer.SerializeToElement(descriptor, WorkerProtocol.jsonOptions)))
                    throw new InvalidOperationException("The worker plugin changed its locked descriptor during configuration.");
                foreach ((ExtensionDescriptor service, Func<IClangFactContributor> factory) in registry.factories)
                {
                    token.ThrowIfCancellationRequested();
                    IClangFactContributor instance = factory() ?? throw new InvalidOperationException("A worker fact factory returned no contributor.");
                    host.m_services.Add(new(service.id, service.priority, instance, entry));
                }
            }
            host.m_services.Sort((
                left,
                right
            ) => left.Priority != right.Priority ? right.Priority.CompareTo(left.Priority) : StringComparer.Ordinal.Compare(left.Id, right.Id));
            return host;
        }
        catch (Exception failure)
        {
            try { await host.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanup) { throw new AggregateException("Worker plugin activation and cleanup both failed.", failure, cleanup); }
            throw;
        }
    }

    internal WorkerPluginCapture Capture(CancellationToken token)
    {
        List<WorkerPluginFingerprint> fingerprints = [];
        List<SourceSnapshot> snapshots = [.. m_preconfigurationInputs];
        List<string> incomplete = [];
        foreach (Entry entry in m_entries)
        {
            new PluginManifestReader(m_host.paths).Verify(entry.Manifest);
            if (!JsonElement.DeepEquals(JsonSerializer.SerializeToElement(entry.Plugin.descriptor, WorkerProtocol.jsonOptions), JsonSerializer.SerializeToElement(entry.Manifest.manifest.plugin, WorkerProtocol.jsonOptions)))
                throw new InvalidOperationException("A worker plugin changed its frozen descriptor during execution.");
            snapshots.Add(new(entry.Manifest.inputs, []) { absentInputs = entry.Manifest.missingInputs.Select(path => new MissingInputDependency(path, PhysicalPathResolver.Resolve(path, m_host.paths))).ToImmutableArray() });
            CaptureService("plugin:" + entry.Manifest.manifest.plugin.id, entry.Plugin, entry);
        }
        foreach (Service service in m_services) CaptureService("service:" + service.Id, service.Instance, service.Owner);
        SourceSnapshot inputs = Merge(snapshots, m_host.paths);
        if (!SourceSnapshotVerifier.Matches(inputs, m_host.paths, token)) throw new IOException("Worker plugin inputs changed during state capture.");
        return new(fingerprints.ToImmutableArray(), inputs, incomplete.ToImmutableArray());

        void CaptureService(
            string id,
            object instance,
            Entry owner
        ) {
            token.ThrowIfCancellationRequested();
            snapshots.Add(CaptureDeclared(instance, token));
            ContentDigest? state = null;
            if (instance is ICacheFingerprintProvider provider)
            {
                CacheFingerprintContribution capture = provider.CaptureFingerprint(m_context!, token)
                    ?? throw new InvalidOperationException("A worker fingerprint provider returned no state.");
                state = new ContentDigest(capture.fingerprint.value);
                snapshots.Add(new ExternalInputSnapshotBuilder().Capture(capture.externalInputs, m_context!.pathBase, m_host.paths, token));
            }
            else incomplete.Add(id);
            string implementation = instance.GetType().AssemblyQualifiedName + ":" + owner.Closure;
            fingerprints.Add(new(id, implementation, state));
        }
    }

    internal static void RequireSame(
        WorkerPluginCapture expected,
        WorkerPluginCapture actual
    ) {
        if (!JsonSerializer.SerializeToUtf8Bytes(expected, WorkerProtocol.jsonOptions).AsSpan()
            .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(actual, WorkerProtocol.jsonOptions)))
            throw new IOException("Worker plugin state or declared inputs changed between discovery, parsing or publication.");
    }

    internal static SourceSnapshot Merge(
        IEnumerable<SourceSnapshot> snapshots,
        IHostPathService paths
    ) {
        SourceSnapshot[] sources = snapshots.ToArray();
        ImmutableArray<T> Unique<T>(
            IEnumerable<T> values,
            Func<T, string> path
        ) {
            List<T> result = [];
            foreach (IGrouping<string, T> group in values.GroupBy(path, paths.comparer).OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                T value = group.First();
                if (group.Any(candidate => !EqualityComparer<T>.Default.Equals(value, candidate))) throw new IOException("A worker input changed between repeated observations.");
                result.Add(value);
            }
            return result.ToImmutableArray();
        }
        SourceSnapshot snapshot = new(Unique(sources.SelectMany(snapshot => snapshot.files), file => file.path),
            Unique(sources.SelectMany(snapshot => snapshot.searchDirectories), directory => directory.path))
        {
            absentInputs = Unique(sources.SelectMany(snapshot => snapshot.absentInputs), missing => missing.path),
            excludedDirectories = sources.SelectMany(snapshot => snapshot.excludedDirectories).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray()
        };
        return InputPathSemanticsSnapshot.Capture(snapshot, paths);
    }

    private SourceSnapshot CaptureDeclared(
        object instance,
        CancellationToken token
    )
        => instance is IExternalInputProvider provider ? new ExternalInputSnapshotBuilder().Capture(
            provider.GetExternalInputs(new(m_context!.configuration, m_context.pathBase), token), m_context.pathBase, m_host.paths, token) : new([], []);

    public async ValueTask DisposeAsync()
    {
        object[] owned = m_plugins.Cast<object>().Concat(m_services.Select(service => (object)service.Instance)).Distinct(ReferenceEqualityComparer.Instance).Reverse().ToArray();
        m_services.Clear(); m_plugins.Clear();
        List<Exception> failures = [];
        foreach (object resource in owned)
        {
            try
            {
                if (resource is IAsyncDisposable asynchronous) await asynchronous.DisposeAsync().ConfigureAwait(false);
                else if (resource is IDisposable disposable) disposable.Dispose();
            }
            catch (Exception failure) { failures.Add(failure); }
        }
        if (failures.Count != 0) throw new AggregateException("Worker plugin cleanup failed.", failures);
    }

    private sealed record Entry(
        LockedPluginManifest Manifest,
        IBindingPlugin Plugin,
        string Closure
    );
    private sealed record Service(
        string Id,
        int Priority,
        IClangFactContributor Instance,
        Entry Owner
    );

    private sealed class Registry(
        PluginDescriptor descriptor,
        ExtensionActivationContext context
    ) : IExtensionRegistryBuilder
    {
        private bool m_open = true;
        private readonly HashSet<string> m_registered = new(StringComparer.Ordinal);
        internal List<(ExtensionDescriptor Descriptor, Func<IClangFactContributor> Factory)> factories { get; } = [];
        public void RegisterFactory<TService>(
            string id,
            Func<ExtensionActivationContext, TService> factory
        ) where TService : class
        {
            if (!m_open) throw new InvalidOperationException("Worker registration is frozen outside Configure.");
            ArgumentNullException.ThrowIfNull(factory);
            ExtensionDescriptor service = descriptor.services.SingleOrDefault(service => service.id == id)
                ?? throw new InvalidOperationException("A worker plugin registered an undeclared service.");
            if (typeof(TService) != typeof(IClangFactContributor) || service.port != ExtensionPort.FrontendFactContributor || !m_registered.Add(id))
                throw new InvalidOperationException("A worker plugin changed its exact fact-contributor port or duplicated a service.");
            factories.Add((service, () => (IClangFactContributor)(object)factory(context)));
        }
        internal void Complete()
        {
            if (m_registered.Count != descriptor.services.Length) throw new InvalidOperationException("A worker plugin omitted a declared fact service.");
        }
        internal void Seal() => m_open = false;
    }
}
