using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Plugins;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Targets;

namespace BGCS.Composition;

internal sealed class GenerationComposition : IAsyncDisposable
{
    private readonly List<IBindingPlugin> m_plugins = [];
    internal FrozenExtensionRegistry registry { get; private set; }

    private GenerationComposition(FrozenExtensionRegistry definition) => registry = definition;

    internal void CaptureFingerprints(
        BGCS.Contracts.Targets.NativeTargetDescriptor target,
        GenerationInputTracker tracker,
        CancellationToken cancellationToken
    ) {
        foreach (IBindingPlugin plugin in m_plugins) tracker.Capture(plugin, target, cancellationToken);
    }

    internal static async ValueTask<GenerationComposition> CreateAsync(
        FrozenExtensionRegistry definition,
        CancellationToken cancellationToken,
        GenerationInputTracker? inputTracker = null
    ) {
        GenerationComposition composition = new(definition);
        BindingHostBuilder builder = BindingHostBuilder.FromFrozen(definition);
        try
        {
            foreach (PluginRegistration registration in definition.Plugins)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (registration.Manifest?.manifest.workerOnly == true)
                {
                    new BGCS.Infrastructure.Plugins.PluginManifestReader().Verify(registration.Manifest);
                    continue;
                }
                IBindingPlugin plugin = registration.Factory() ?? throw new InvalidOperationException("A session plugin factory returned no plugin.");
                composition.m_plugins.Add(plugin);
                inputTracker?.CaptureDeclared(plugin, cancellationToken);
                CheckDescriptor(plugin, registration);
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
            composition.registry = builder.Freeze() with { WorkerPlugins = definition.WorkerPlugins };
            return composition;
        }
        catch (Exception failure)
        {
            try { await composition.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanup) { throw new AggregateException("Plugin activation and cleanup failed before registry publication.", failure, cleanup); }
            throw;
        }
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
        IBindingPlugin[] plugins = m_plugins.ToArray();
        m_plugins.Clear();
        List<Exception> failures = [];
        for (int index = plugins.Length - 1; index >= 0; index--)
            try
            {
                if (plugins[index] is IAsyncDisposable asynchronous) await asynchronous.DisposeAsync().ConfigureAwait(false);
                else plugins[index].Dispose();
            }
            catch (Exception failure) { failures.Add(failure); }
        if (failures.Count != 0) throw new AggregateException("Session plugin cleanup failed.", failures);
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
            Func<TService> factory
        ) where TService : class
        {
            if (!m_open) throw new InvalidOperationException("The plugin registration surface is frozen outside Configure.");
            ArgumentNullException.ThrowIfNull(factory);
            ExtensionDescriptor service = descriptor.services.SingleOrDefault(service => service.id == id)
                ?? throw new InvalidOperationException($"Plugin '{descriptor.id}' registered undeclared service '{id}'.");
            if (!m_registered.Add(id)) throw new InvalidOperationException($"Plugin service '{id}' was registered more than once.");
            ExtensionPort actualPort = Port(typeof(TService));
            if (service.port != actualPort) throw new InvalidOperationException($"Plugin service '{id}' changed its declared neutral port.");
            switch (service.port)
            {
                case ExtensionPort.Frontend: builder.WithFrontendFactory(() => (IFrontend)(object)factory()); break;
                case ExtensionPort.NativeTargetProvider: builder.AddTargetProvider(id, () => (INativeTargetProvider)(object)factory()); break;
                case ExtensionPort.NativeModuleTransform: builder.AddNativeTransform(id, service.priority, () => (INativeModuleTransform)(object)factory()); break;
                case ExtensionPort.CppTypeLowering: builder.AddCppTypeLowering(id, service.priority, () => (ICppTypeLowering)(object)factory()); break;
                case ExtensionPort.CppCallableLowering: builder.AddCppCallableLowering(id, service.priority, () => (ICppCallableLowering)(object)factory()); break;
                case ExtensionPort.InteropTransform: builder.AddInteropTransform(id, service.priority, () => (IInteropTransform)(object)factory()); break;
                case ExtensionPort.ManagedApiTransform: builder.AddManagedTransform(id, service.priority, () => (IManagedApiTransform)(object)factory()); break;
                case ExtensionPort.ManagedProjection: builder.WithManagedProjectionFactory(() => (IManagedProjection)(object)factory()); break;
                case ExtensionPort.BindingBackend: builder.WithBackendFactory(() => (IBindingBackend)(object)factory()); break;
                case ExtensionPort.GenerationValidator: builder.AddValidator(id, service.priority, () => (IGenerationValidator)(object)factory()); break;
                case ExtensionPort.ArtifactProcessor: builder.AddArtifactProcessor(id, service.priority, () => (IArtifactProcessor)(object)factory()); break;
                case ExtensionPort.SourceContributor: builder.AddSourceContributor(id, service.priority, () => (ISourceContributor)(object)factory()); break;
                default: throw new InvalidOperationException("The plugin declared an unavailable neutral port.");
            }
        }

        internal void Complete()
        {
            if (m_registered.Count != descriptor.services.Length) throw new InvalidOperationException($"Plugin '{descriptor.id}' did not register its complete declared service collection.");
        }

        internal void Seal() => m_open = false;

        private static ExtensionPort Port(Type contract)
        {
            if (contract == typeof(IFrontend)) return ExtensionPort.Frontend;
            if (contract == typeof(INativeTargetProvider)) return ExtensionPort.NativeTargetProvider;
            if (contract == typeof(INativeModuleTransform)) return ExtensionPort.NativeModuleTransform;
            if (contract == typeof(ICppTypeLowering)) return ExtensionPort.CppTypeLowering;
            if (contract == typeof(ICppCallableLowering)) return ExtensionPort.CppCallableLowering;
            if (contract == typeof(IInteropTransform)) return ExtensionPort.InteropTransform;
            if (contract == typeof(IManagedApiTransform)) return ExtensionPort.ManagedApiTransform;
            if (contract == typeof(IManagedProjection)) return ExtensionPort.ManagedProjection;
            if (contract == typeof(IBindingBackend)) return ExtensionPort.BindingBackend;
            if (contract == typeof(IGenerationValidator)) return ExtensionPort.GenerationValidator;
            if (contract == typeof(IArtifactProcessor)) return ExtensionPort.ArtifactProcessor;
            if (contract == typeof(ISourceContributor)) return ExtensionPort.SourceContributor;
            throw new InvalidOperationException("A standard plugin can register only exact neutral extension interfaces.");
        }
    }
}
