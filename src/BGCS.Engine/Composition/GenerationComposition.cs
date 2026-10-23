using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Plugins;
using BGCS.Core.Hosting;
using System.Text.Json;
using BGCS.Core.Ports;
using BGCS.Core.Targets;

namespace BGCS.Composition;

internal sealed class GenerationComposition : IAsyncDisposable
{
    private readonly List<IBindingPlugin> m_plugins = [];
    private readonly SessionServiceScope m_services = new();
    private IHostServicesProvider? m_hostProvider;
    internal HostServices hostServices { get; private set; } = null!;
    internal FrozenExtensionRegistry registry { get; private set; }

    private GenerationComposition(FrozenExtensionRegistry definition) => registry = definition;

    internal static async ValueTask<GenerationComposition> CreateHostAsync(
        FrozenExtensionRegistry definition,
        string baseDirectory
    ) {
        GenerationComposition composition = new(definition);
        try
        {
            composition.registry = definition with { Activation = new(null, baseDirectory) };
            composition.m_hostProvider = composition.registry.Host();
            composition.hostServices = composition.m_hostProvider.Create(new(baseDirectory,
                JsonSerializer.SerializeToElement(new Dictionary<string, string>())))
                ?? throw new InvalidOperationException("The host provider returned no owned host facilities.");
            composition.registry = composition.registry with { Activation = new(composition.hostServices, baseDirectory), Scope = composition.m_services };
            return composition;
        }
        catch (Exception failure)
        {
            try { await composition.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanup) { throw new AggregateException("Host activation and cleanup failed before configuration loading.", failure, cleanup); }
            throw;
        }
    }

    internal void CaptureFingerprints(
        BGCS.Core.Targets.NativeTargetDescriptor target,
        GenerationInputTracker tracker,
        CancellationToken cancellationToken
    ) {
        foreach (IBindingPlugin plugin in m_plugins) tracker.Capture(plugin, target, cancellationToken);
        tracker.Capture(m_hostProvider!, target, cancellationToken);
    }

    internal static async ValueTask<GenerationComposition> CreateAsync(
        FrozenExtensionRegistry definition,
        string baseDirectory,
        CancellationToken cancellationToken,
        GenerationInputTracker? inputTracker = null
    ) {
        GenerationComposition composition = await CreateHostAsync(definition, baseDirectory).ConfigureAwait(false);
        try
        {
            await composition.ConfigureAsync(definition, inputTracker, cancellationToken).ConfigureAwait(false);
            return composition;
        }
        catch (Exception failure)
        {
            try { await composition.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanup) { throw new AggregateException("Extension activation and cleanup failed before candidate generation.", failure, cleanup); }
            throw;
        }
    }

    internal ValueTask ConfigureAsync(
        FrozenExtensionRegistry definition,
        GenerationInputTracker? inputTracker,
        CancellationToken cancellationToken
    ) {
        BindingHostBuilder builder = BindingHostBuilder.FromFrozen(definition);
        inputTracker?.BindHost(hostServices.paths);
        inputTracker?.CaptureDeclared(m_hostProvider!, cancellationToken);
        foreach (PluginRegistration registration in definition.Plugins)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (registration.Manifest?.manifest.workerOnly == true)
            {
                continue;
            }
            IBindingPlugin plugin = registration.Factory(registry.Activation) ?? throw new InvalidOperationException("A session plugin factory returned no plugin.");
            m_plugins.Add(plugin);
            m_services.Own(plugin);
            CheckDescriptor(plugin, registration);
            inputTracker?.CaptureDeclared(plugin, cancellationToken);
            Registry adapter = new(builder, registration.Descriptor);
            try
            {
                plugin.Configure(adapter, registration.Configuration ?? throw new InvalidOperationException("A plugin omitted its frozen settings namespace."));
                adapter.Complete();
                CheckDescriptor(plugin, registration);
            }
            finally { adapter.Seal(); }
        }
        cancellationToken.ThrowIfCancellationRequested();
        registry = builder.Freeze() with
        { WorkerPlugins = definition.WorkerPlugins, Activation = registry.Activation, Scope = m_services };
        foreach (PluginRegistration registration in definition.Plugins)
            if (registration.Manifest is not null)
                new BGCS.Infrastructure.Plugins.PluginManifestReader(hostServices.paths).Verify(registration.Manifest);
        return ValueTask.CompletedTask;
    }

    private static void CheckDescriptor(
        IBindingPlugin plugin,
        PluginRegistration registration
    ) {
        PluginDescriptor actual = PluginDescriptorValidator.Freeze(plugin.descriptor);
        PluginDescriptor expected = registration.Descriptor;
        if (actual.id != expected.id || actual.version != expected.version || actual.contractsMajor != expected.contractsMajor
            || actual.minimumApiMinor != expected.minimumApiMinor || !actual.services.SequenceEqual(expected.services)
            || !System.Text.Json.JsonElement.DeepEquals(actual.configurationSchema, expected.configurationSchema))
            throw new InvalidOperationException($"Plugin '{expected.id}' changed its frozen discovery descriptor during session activation.");
    }

    public async ValueTask DisposeAsync()
    {
        List<Exception> failures = [];
        try { await DisposeGenerationServicesAsync().ConfigureAwait(false); } catch (Exception failure) { failures.Add(failure); }
        try { if (hostServices is not null) await hostServices.DisposeAsync().ConfigureAwait(false); } catch (Exception failure) { failures.Add(failure); }
        if (m_hostProvider is IAsyncDisposable asynchronousHost)
            try { await asynchronousHost.DisposeAsync().ConfigureAwait(false); } catch (Exception failure) { failures.Add(failure); }
        else if (m_hostProvider is IDisposable disposableHost)
            try { disposableHost.Dispose(); } catch (Exception failure) { failures.Add(failure); }
        m_hostProvider = null;
        if (failures.Count != 0) throw new AggregateException("Session extension cleanup failed.", failures);
    }

    internal ValueTask ReleaseServiceAsync(object service) => m_services.ReleaseAsync(service);

    internal async ValueTask DisposeGenerationServicesAsync()
    {
        await m_services.DisposeAsync().ConfigureAwait(false);
        m_plugins.Clear();
    }

    private sealed class Registry(
        BindingHostBuilder builder,
        PluginDescriptor descriptor
    ) : IExtensionRegistryBuilder
    {
        private readonly HashSet<string> m_registered = new(StringComparer.Ordinal);
        private bool m_open = true;

        public void RegisterFactory<TService>(
            string id,
            Func<ExtensionActivationContext, TService> factory
        ) where TService : class
        {
            if (!m_open) throw new InvalidOperationException("The plugin registration surface is frozen outside Configure.");
            ArgumentNullException.ThrowIfNull(factory);
            ExtensionDescriptor service = descriptor.services.SingleOrDefault(service => service.id == id)
                ?? throw new InvalidOperationException($"Plugin '{descriptor.id}' registered undeclared service '{id}'.");
            if (!m_registered.Add(id)) throw new InvalidOperationException($"Plugin service '{id}' was registered more than once.");
            ExtensionPort actualPort = ExtensionPortResolver.Resolve(typeof(TService));
            if (service.port != actualPort) throw new InvalidOperationException($"Plugin service '{id}' changed its declared neutral port.");
            builder.RegisterFactory(service, factory);
        }

        internal void Complete()
        {
            if (m_registered.Count != descriptor.services.Length) throw new InvalidOperationException($"Plugin '{descriptor.id}' did not register its complete declared service collection.");
        }

        internal void Seal() => m_open = false;

    }
}
