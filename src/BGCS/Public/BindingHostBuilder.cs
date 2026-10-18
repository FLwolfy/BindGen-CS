using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Contracts.Ports;
using BGCS.Infrastructure.Workers;
using BGCS.Contracts.Plugins;
using BGCS.Contracts.Targets;
using BGCS.Composition;
using BGCS.Infrastructure.Plugins;
using System.Text.Json;
using BGCS.Infrastructure.Configuration;

namespace BGCS;

/// <summary>Builds a host whose frontend factory and execution settings cannot change during sessions.</summary>
public sealed class BindingHostBuilder
{
    private Func<IFrontend>? m_frontend;
    private string? m_workerRoot;
    private TimeSpan m_parseTimeout = TimeSpan.FromSeconds(300);
    private bool m_builtIns;
    private Func<IManagedProjection>? m_projection;
    private Func<IBindingBackend>? m_backend;
    private bool m_frontendConfigured;
    private bool m_projectionConfigured;
    private bool m_backendConfigured;
    private readonly Dictionary<string, (int Priority, Func<INativeModuleTransform> Factory)> m_transforms = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (int Priority, Func<IInteropTransform> Factory)> m_interopTransforms = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (int Priority, Func<IManagedApiTransform> Factory)> m_managedTransforms = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (int Priority, Func<ICppTypeLowering> Factory)> m_cppTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (int Priority, Func<ICppCallableLowering> Factory)> m_cppCallables = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<INativeTargetProvider>> m_targets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (int Priority, Func<IGenerationValidator> Factory)> m_validators = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (int Priority, Func<IArtifactProcessor> Factory)> m_processors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PluginRegistration> m_plugins = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (int Priority, Func<ISourceContributor> Factory)> m_sourceContributors = new(StringComparer.Ordinal);

    /// <summary>Enables the built-in target resolver and isolated Clang frontend adapter.</summary>
    /// <returns>This builder.</returns>
    public BindingHostBuilder AddBuiltIns() { m_builtIns = true; return this; }

    /// <summary>Selects an explicitly deployed worker asset directory.</summary>
    /// <param name="root">The directory containing the worker's execution closure.</param>
    /// <returns>This builder.</returns>
    public BindingHostBuilder WithWorkerAssetRoot(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        m_workerRoot = System.IO.Path.GetFullPath(root);
        return this;
    }

    /// <summary>Selects a positive deadline for each parser request.</summary>
    /// <param name="timeout">The parse deadline.</param>
    /// <returns>This builder.</returns>
    public BindingHostBuilder WithParseTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        m_parseTimeout = timeout;
        return this;
    }

    /// <summary>Creates an independent custom frontend instance for every generation session.</summary>
    /// <param name="factory">The session frontend factory.</param>
    /// <returns>This builder.</returns>
    public BindingHostBuilder WithFrontendFactory(Func<IFrontend> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (m_frontendConfigured)
            throw new InvalidOperationException("A primary frontend factory is already registered.");
        m_frontend = factory;
        m_frontendConfigured = true;
        return this;
    }

    /// <summary>Registers the primary managed projection with independent session-owned instances.</summary>
    /// <param name="factory">The projection service factory.</param>
    /// <returns>This builder.</returns>
    public BindingHostBuilder WithManagedProjectionFactory(Func<IManagedProjection> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (m_projectionConfigured) throw new InvalidOperationException("A primary managed projection is already registered.");
        m_projection = factory;
        m_projectionConfigured = true;
        return this;
    }

    /// <summary>Registers the primary source backend with independent session-owned instances.</summary>
    /// <param name="factory">The backend service factory.</param>
    /// <returns>This builder.</returns>
    public BindingHostBuilder WithBackendFactory(Func<IBindingBackend> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (m_backendConfigured) throw new InvalidOperationException("A primary binding backend is already registered.");
        m_backend = factory;
        m_backendConfigured = true;
        return this;
    }

    /// <summary>Registers one neutral native-fact transformation with a session-owned service factory.</summary>
    /// <param name="id">The unique registered service identity.</param>
    /// <param name="priority">Execution priority, followed by ordinal identity for ties.</param>
    /// <param name="factory">The independent session service factory.</param>
    /// <returns>This builder.</returns>
    public BindingHostBuilder AddNativeTransform(
        string id,
        int priority,
        Func<INativeModuleTransform> factory
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(factory);
        if (Registered(id) || !m_transforms.TryAdd(id, (priority, factory)))
            throw new InvalidOperationException($"Native transformation '{id}' is already registered.");
        return this;
    }

    /// <summary>Registers a session-owned neutral interoperability transformation.</summary>
    /// <param name="id">The globally unique transformation identity.</param>
    /// <param name="priority">Execution priority followed by stable ordinal identity.</param>
    /// <param name="factory">The independent session service factory.</param>
    /// <returns>This builder.</returns>
    public BindingHostBuilder AddInteropTransform(
        string id,
        int priority,
        Func<IInteropTransform> factory
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(factory);
        if (Registered(id) || !m_interopTransforms.TryAdd(id, (priority, factory)))
            throw new InvalidOperationException($"Transformation '{id}' is already registered.");
        return this;
    }

    /// <summary>Registers a composable managed presentation change without replacing the primary projection or native ABI.</summary>
    /// <param name="id">The globally unique transformation identity.</param>
    /// <param name="priority">Descending execution priority followed by stable ordinal identity.</param>
    /// <param name="factory">The independent session service factory.</param>
    /// <returns>This builder.</returns>
    public BindingHostBuilder AddManagedTransform(
        string id,
        int priority,
        Func<IManagedApiTransform> factory
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(factory);
        if (Registered(id) || !m_managedTransforms.TryAdd(id, (priority, factory)))
            throw new InvalidOperationException($"Managed transformation '{id}' is already registered.");
        return this;
    }

    /// <summary>Registers a neutral session-owned C++ type lowering without a frontend dependency.</summary>
    /// <param name="id">The globally unique extension service identity.</param>
    /// <param name="priority">The frozen matching priority.</param>
    /// <param name="factory">The independent per-session service factory.</param>
    /// <returns>This builder.</returns>
    public BindingHostBuilder AddCppTypeLowering(
        string id,
        int priority,
        Func<ICppTypeLowering> factory
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(factory);
        if (Registered(id)) throw new InvalidOperationException($"Extension service '{id}' is already registered.");
        m_cppTypes.Add(id, (priority, factory));
        return this;
    }

    /// <summary>Registers a neutral session-owned callable lowering and its typed helper operations.</summary>
    /// <param name="id">The globally unique extension service identity.</param>
    /// <param name="priority">The frozen matching priority.</param>
    /// <param name="factory">The independent per-session service factory.</param>
    /// <returns>This builder.</returns>
    public BindingHostBuilder AddCppCallableLowering(
        string id,
        int priority,
        Func<ICppCallableLowering> factory
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(factory);
        if (Registered(id)) throw new InvalidOperationException($"Extension service '{id}' is already registered.");
        m_cppCallables.Add(id, (priority, factory));
        return this;
    }

    /// <summary>Registers a session-owned provider for a new native target family.</summary>
    /// <param name="id">The globally unique provider identity.</param>
    /// <param name="factory">The independent target provider factory.</param>
    /// <returns>This builder.</returns>
    public BindingHostBuilder AddTargetProvider(
        string id,
        Func<INativeTargetProvider> factory
    ) {
        PluginDescriptorValidator.ValidateId(id);
        ArgumentNullException.ThrowIfNull(factory);
        if (Registered(id)) throw new InvalidOperationException($"Extension service '{id}' is already registered.");
        m_targets.Add(id, factory);
        return this;
    }

    /// <summary>Registers an independently owned final candidate gate for every selected native target.</summary>
    /// <param name="id">The globally unique validation gate identity.</param>
    /// <param name="priority">Descending execution order, followed by ordinal identity.</param>
    /// <param name="factory">The independent validator factory.</param>
    /// <returns>This builder.</returns>
    public BindingHostBuilder AddValidator(
        string id,
        int priority,
        Func<IGenerationValidator> factory
    ) {
        PluginDescriptorValidator.ValidateId(id);
        ArgumentNullException.ThrowIfNull(factory);
        if (Registered(id)) throw new InvalidOperationException($"Extension service '{id}' is already registered.");
        m_validators.Add(id, (priority, factory));
        return this;
    }

    /// <summary>Registers independent candidate text processing before final contract and build checks.</summary>
    /// <param name="id">The globally unique processor identity.</param>
    /// <param name="priority">Descending execution priority, followed by ordinal identity.</param>
    /// <param name="factory">The independent processor factory.</param>
    /// <returns>This builder.</returns>
    public BindingHostBuilder AddArtifactProcessor(
        string id,
        int priority,
        Func<IArtifactProcessor> factory
    ) {
        PluginDescriptorValidator.ValidateId(id);
        ArgumentNullException.ThrowIfNull(factory);
        if (Registered(id)) throw new InvalidOperationException($"Extension service '{id}' is already registered.");
        m_processors.Add(id, (priority, factory));
        return this;
    }

    /// <summary>Registers immutable native, managed and resource inputs produced once before each original target parse.</summary>
    /// <param name="id">The globally unique portable service identity used for its owned contribution namespace.</param>
    /// <param name="priority">Descending execution priority, followed by ordinal identity.</param>
    /// <param name="factory">The independently owned generation-session source service.</param>
    /// <returns>This builder.</returns>
    public BindingHostBuilder AddSourceContributor(
        string id,
        int priority,
        Func<ISourceContributor> factory
    ) {
        SourceContributionCapture.ValidateServiceId(id);
        ArgumentNullException.ThrowIfNull(factory);
        if (Registered(id)) throw new InvalidOperationException($"Extension service '{id}' is already registered.");
        m_sourceContributors.Add(id, (priority, factory));
        return this;
    }

    /// <summary>Discovers immutable plugin metadata and creates an independent plugin for each session.</summary>
    /// <typeparam name="TPlugin">A Contracts-only neutral plugin with a public parameterless constructor.</typeparam>
    /// <param name="settings">An optional immutable plugin settings object.</param>
    /// <param name="pathBase">The captured settings input base, or the current directory at registration.</param>
    /// <returns>This builder.</returns>
    public BindingHostBuilder AddPlugin<TPlugin>(
        JsonElement? settings = null,
        string? pathBase = null
    ) where TPlugin : IBindingPlugin, new()
        => AddPluginFactory(() => new TPlugin(), settings, pathBase);

    /// <summary>Captures neutral plugin metadata without retaining its discovery instance.</summary>
    /// <param name="factory">The independent discovery and session plugin factory.</param>
    /// <param name="settings">The optional plugin settings object validated against its own declared schema.</param>
    /// <param name="pathBase">The captured settings input base, or the current directory at registration.</param>
    /// <returns>This builder.</returns>
    public BindingHostBuilder AddPluginFactory(
        Func<IBindingPlugin> factory,
        JsonElement? settings = null,
        string? pathBase = null
    ) {
        ArgumentNullException.ThrowIfNull(factory);
        PluginDescriptor descriptor;
        using (IBindingPlugin discovery = factory() ?? throw new InvalidOperationException("The plugin factory returned no discovery instance."))
            descriptor = PluginDescriptorValidator.Freeze(discovery.descriptor);
        return RegisterPlugin(descriptor, factory, settings, pathBase);
    }

    /// <summary>Registers predeclared immutable metadata without executing a discovery constructor or loading a parser.</summary>
    /// <param name="descriptor">The complete declared contracts, services and settings schema, independently cloned before registration.</param>
    /// <param name="factory">The independent session factory, whose implementation descriptor is checked before Configure.</param>
    /// <param name="settings">The optional plugin settings object.</param>
    /// <param name="pathBase">The captured settings input base.</param>
    /// <returns>This builder.</returns>
    public BindingHostBuilder AddPluginFactory(
        PluginDescriptor descriptor,
        Func<IBindingPlugin> factory,
        JsonElement? settings = null,
        string? pathBase = null
    )
        => RegisterPlugin(PluginDescriptorValidator.Freeze(descriptor), factory, settings, pathBase);

    private BindingHostBuilder RegisterPlugin(
        PluginDescriptor descriptor,
        Func<IBindingPlugin> factory,
        JsonElement? settings,
        string? pathBase,
        bool workerOnly = false
    ) {
        ArgumentNullException.ThrowIfNull(factory);
        if (workerOnly ? descriptor.services.Any(service => service.port != ExtensionPort.FrontendFactContributor)
            : descriptor.services.Any(service => service.port == ExtensionPort.FrontendFactContributor))
            throw new InvalidOperationException("Parser-specific fact contributors require a worker-only manifest containing only frontend fact services.");
        if (m_plugins.ContainsKey(descriptor.id)) throw new InvalidOperationException($"Plugin '{descriptor.id}' is already registered.");
        if (descriptor.services.Any(service => Registered(service.id))) throw new InvalidOperationException("A plugin service conflicts with a globally registered service identity.");
        JsonElement values = settings?.Clone() ?? JsonSerializer.SerializeToElement(ImmutableDictionary<string, string>.Empty);
        new ExtensionSchemaValidator().Validate(descriptor.configurationSchema, values, descriptor.id);
        m_plugins.Add(descriptor.id, new(descriptor, factory) { Configuration = new(descriptor.id, values, System.IO.Path.GetFullPath(pathBase ?? Environment.CurrentDirectory)) });
        return this;
    }

    /// <summary>Locks an explicit local plugin entry and activates it in a private context for every session.</summary>
    /// <param name="path">The local plugin manifest and its executable closure.</param>
    /// <param name="settings">The optional plugin settings object.</param>
    /// <param name="pathBase">The optional settings input base, defaulting to the manifest directory.</param>
    /// <returns>This builder.</returns>
    public BindingHostBuilder AddPluginManifest(
        string path,
        JsonElement? settings = null,
        string? pathBase = null
    ) => AddPluginManifest(new PluginManifestReader().Read(path), settings, pathBase);

    internal BindingHostBuilder AddPluginManifest(
        LockedPluginManifest manifest,
        JsonElement? settings = null,
        string? pathBase = null
    ) {
        new PluginManifestReader().Verify(manifest);
        bool workerOnly = manifest.manifest.workerOnly;
        if (workerOnly ? manifest.manifest.workerParserIdentity != WorkerProtocol.C_PARSER_IDENTITY : manifest.manifest.workerParserIdentity is not null)
            throw new InvalidOperationException("A worker-only plugin requires the exact deployed parser identity; ordinary plugins cannot declare one.");
        RegisterPlugin(PluginDescriptorValidator.Freeze(manifest.manifest.plugin),
            workerOnly ? () => throw new InvalidOperationException("A worker-only plugin cannot be activated in the SDK Host.") : () => new PluginLoader().Activate(manifest),
            settings, pathBase ?? System.IO.Path.GetDirectoryName(manifest.path), workerOnly);
        string id = manifest.manifest.plugin.id;
        m_plugins[id] = m_plugins[id] with { Manifest = manifest };
        return this;
    }

    internal void WithPluginSettings(
        string id,
        JsonElement settings,
        string pathBase,
        System.Collections.Immutable.ImmutableDictionary<string, string> valuePathBases
    ) {
        if (!m_plugins.TryGetValue(id, out PluginRegistration? registration)) throw new ArgumentException($"Settings namespace '{id}' has no registered plugin.");
        new ExtensionSchemaValidator().Validate(registration.Descriptor.configurationSchema, settings, id);
        m_plugins[id] = registration with { Configuration = new(id, settings, System.IO.Path.GetFullPath(pathBase), valuePathBases) };
    }

    private bool Registered(string id) => m_transforms.ContainsKey(id) || m_interopTransforms.ContainsKey(id) || m_managedTransforms.ContainsKey(id) || m_cppTypes.ContainsKey(id) || m_cppCallables.ContainsKey(id)
        || m_targets.ContainsKey(id) || m_validators.ContainsKey(id) || m_processors.ContainsKey(id) || m_sourceContributors.ContainsKey(id)
        || m_plugins.ContainsKey(id) || m_plugins.Values.Any(plugin => plugin.Descriptor.services.Any(service => service.id == id));

    /// <summary>Freezes the frontend composition and creates an independently owned host.</summary>
    /// <returns>The caller-owned host.</returns>
    public BindingHost Build() => new(Freeze());

    internal FrozenExtensionRegistry Freeze()
    {
        if (!m_builtIns)
            throw new InvalidOperationException("Built-in target services must be registered before building the host.");
        string? root = m_workerRoot;
        TimeSpan timeout = m_parseTimeout;
        PluginRegistration[] plugins = m_plugins.Values.OrderBy(plugin => plugin.Descriptor.id, StringComparer.Ordinal).ToArray();
        foreach ((ExtensionPort port, bool configured) in new[] { (ExtensionPort.Frontend, m_frontendConfigured), (ExtensionPort.ManagedProjection, m_projectionConfigured), (ExtensionPort.BindingBackend, m_backendConfigured) })
            if (plugins.Sum(plugin => plugin.Descriptor.services.Count(service => service.port == port)) + (configured ? 1 : 0) > 1)
                throw new InvalidOperationException($"More than one primary '{port}' service is registered.");
        return new(m_frontend ?? (() => new FrontendWorkerClient(WorkerAssetLocator.Locate(root), timeout)), m_frontendConfigured,
            m_projection ?? (() => new BGCS.CSharp.Projection.DefaultManagedProjection()), m_projectionConfigured,
            m_backend ?? (() => new BGCS.CSharp.CSharpBackend()), m_backendConfigured,
            m_transforms.OrderByDescending(entry => entry.Value.Priority).ThenBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new BindingHost.NativeTransformRegistration(entry.Key, entry.Value.Priority, entry.Value.Factory)).ToImmutableArray(),
            m_interopTransforms.OrderByDescending(entry => entry.Value.Priority).ThenBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new BindingHost.InteropTransformRegistration(entry.Key, entry.Value.Priority, entry.Value.Factory)).ToImmutableArray(),
            m_cppTypes.OrderByDescending(entry => entry.Value.Priority).ThenBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new BindingHost.CppTypeRegistration(entry.Key, entry.Value.Priority, entry.Value.Factory)).ToImmutableArray(),
            m_cppCallables.OrderByDescending(entry => entry.Value.Priority).ThenBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new BindingHost.CppCallableRegistration(entry.Key, entry.Value.Priority, entry.Value.Factory)).ToImmutableArray(),
            m_validators.OrderByDescending(entry => entry.Value.Priority).ThenBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => new ValidatorRegistration(entry.Key, entry.Value.Priority, entry.Value.Factory)).ToImmutableArray(),
            m_processors.OrderByDescending(entry => entry.Value.Priority).ThenBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => new ProcessorRegistration(entry.Key, entry.Value.Priority, entry.Value.Factory)).ToImmutableArray(),
            m_targets.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => new TargetProviderRegistration(entry.Key, entry.Value)).ToImmutableArray(),
            m_sourceContributors.OrderByDescending(entry => entry.Value.Priority).ThenBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new SourceContributorRegistration(entry.Key, entry.Value.Priority, entry.Value.Factory)).ToImmutableArray(),
            plugins.ToImmutableArray())
        {
            ParseTimeout = timeout,
            WorkerPlugins = plugins.Where(plugin => plugin.Manifest?.manifest.workerOnly == true).ToImmutableArray(),
            ManagedTransforms = m_managedTransforms.OrderByDescending(entry => entry.Value.Priority).ThenBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new BindingHost.ManagedTransformRegistration(entry.Key, entry.Value.Priority, entry.Value.Factory)).ToImmutableArray()
        };
    }

    internal static BindingHostBuilder FromFrozen(
        FrozenExtensionRegistry registry,
        bool includePlugins = false
    ) {
        BindingHostBuilder builder = new()
        {
            m_builtIns = true,
            m_frontend = registry.Frontend,
            m_frontendConfigured = registry.FrontendConfigured,
            m_projection = registry.Projection,
            m_projectionConfigured = registry.ProjectionConfigured,
            m_backend = registry.Backend,
            m_backendConfigured = registry.BackendConfigured,
            m_parseTimeout = registry.ParseTimeout
        };
        foreach (BindingHost.NativeTransformRegistration service in registry.NativeTransforms) builder.m_transforms.Add(service.Id, (service.Priority, service.Factory));
        foreach (BindingHost.InteropTransformRegistration service in registry.InteropTransforms) builder.m_interopTransforms.Add(service.Id, (service.Priority, service.Factory));
        foreach (BindingHost.ManagedTransformRegistration service in registry.ManagedTransforms) builder.m_managedTransforms.Add(service.Id, (service.Priority, service.Factory));
        foreach (BindingHost.CppTypeRegistration service in registry.CppTypes) builder.m_cppTypes.Add(service.Id, (service.Priority, service.Factory));
        foreach (BindingHost.CppCallableRegistration service in registry.CppCallables) builder.m_cppCallables.Add(service.Id, (service.Priority, service.Factory));
        foreach (TargetProviderRegistration service in registry.Targets) builder.m_targets.Add(service.Id, service.Factory);
        foreach (ValidatorRegistration service in registry.Validators) builder.m_validators.Add(service.Id, (service.Priority, service.Factory));
        foreach (ProcessorRegistration service in registry.Processors) builder.m_processors.Add(service.Id, (service.Priority, service.Factory));
        foreach (SourceContributorRegistration service in registry.SourceContributors) builder.m_sourceContributors.Add(service.Id, (service.Priority, service.Factory));
        if (includePlugins)
            foreach (PluginRegistration plugin in registry.Plugins) builder.m_plugins.Add(plugin.Descriptor.id, plugin);
        return builder;
    }
}
