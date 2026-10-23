using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using BGCS.Core.Hosting;
using BGCS.Core.Plugins;
using BGCS.Composition;
using BGCS.Infrastructure.Configuration;
using BGCS.Infrastructure.Plugins;

namespace BGCS;

/// <summary>Freezes explicit extension descriptors and independent session factories.</summary>
public sealed class BindingHostBuilder
{
    private readonly Dictionary<string, ServiceRegistration> m_services = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PluginRegistration> m_plugins = new(StringComparer.Ordinal);

    /// <summary>Registers a service through the same authoritative descriptor used by plugins.</summary>
    /// <typeparam name="TService">The exact neutral extension port.</typeparam>
    /// <param name="descriptor">The unique service identity, port and ordering policy.</param>
    /// <param name="factory">An independent session-owned service factory.</param>
    /// <returns>This builder.</returns>
    public BindingHostBuilder RegisterFactory<TService>(
        ExtensionDescriptor descriptor,
        Func<ExtensionActivationContext, TService> factory
    )
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(factory);
        PluginDescriptorValidator.ValidateId(descriptor.id);
        if (ExtensionPortResolver.Resolve(typeof(TService)) != descriptor.port)
            throw new InvalidOperationException("The factory type does not match its declared extension port.");
        if (Registered(descriptor.id))
            throw new InvalidOperationException($"Extension service '{descriptor.id}' is already registered.");
        m_services.Add(descriptor.id, new(descriptor, context => factory(context)
            ?? throw new InvalidOperationException($"Extension service '{descriptor.id}' returned no instance.")));
        return this;
    }

    /// <summary>Explicitly replaces one directly registered service before the composition is frozen.</summary>
    /// <typeparam name="TService">The exact extension port being replaced.</typeparam>
    /// <param name="replacedId">The existing service identity, which must belong to the same port.</param>
    /// <param name="descriptor">The replacement identity and ordering descriptor.</param>
    /// <param name="factory">The independent session factory.</param>
    /// <returns>This builder.</returns>
    public BindingHostBuilder ReplaceFactory<TService>(
        string replacedId,
        ExtensionDescriptor descriptor,
        Func<ExtensionActivationContext, TService> factory
    ) where TService : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(replacedId);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(factory);
        ExtensionPort port = ExtensionPortResolver.Resolve(typeof(TService));
        if (descriptor.port != port) throw new InvalidOperationException("The replacement changes its declared extension port.");
        if (!m_services.TryGetValue(replacedId, out ServiceRegistration? previous))
            throw new InvalidOperationException($"Service '{replacedId}' is not a directly registered replaceable service.");
        if (previous.Descriptor.port != port) throw new InvalidOperationException("The replaced service belongs to a different port.");
        PluginDescriptorValidator.ValidateId(descriptor.id);
        if (descriptor.id != replacedId && Registered(descriptor.id)) throw new InvalidOperationException("The replacement service identity is already registered.");
        m_services.Remove(replacedId);
        return RegisterFactory(descriptor, factory);
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
        if (descriptor.services.Any(service => service.port == ExtensionPort.HostServicesProvider))
            throw new InvalidOperationException("Select the host provider through RegisterFactory before reading configuration or extension manifests.");
        m_plugins.Add(descriptor.id, new(descriptor, _ => factory()) { Configuration = new(descriptor.id, values, System.IO.Path.GetFullPath(pathBase ?? Environment.CurrentDirectory)) });
        return this;
    }

    /// <summary>Locks an explicit local plugin entry and activates it in a private context for every session.</summary>
    /// <param name="path">The local plugin manifest and its executable closure.</param>
    /// <param name="paths">The explicitly selected host path implementation.</param>
    /// <param name="settings">The optional plugin settings object.</param>
    /// <param name="pathBase">The optional settings input base, defaulting to the manifest directory.</param>
    /// <returns>This builder.</returns>
    public BindingHostBuilder AddPluginManifest(
        string path,
        IHostPathService paths,
        JsonElement? settings = null,
        string? pathBase = null
    ) => AddPluginManifest(new PluginManifestReader(paths).Read(path), settings, pathBase);

    internal BindingHostBuilder AddPluginManifest(
        LockedPluginManifest manifest,
        JsonElement? settings = null,
        string? pathBase = null
    ) {

        bool workerOnly = manifest.manifest.workerOnly;
        if (workerOnly ? string.IsNullOrWhiteSpace(manifest.manifest.workerParserIdentity) : manifest.manifest.workerParserIdentity is not null)
            throw new InvalidOperationException("A worker-only plugin requires the exact deployed parser identity; ordinary plugins cannot declare one.");
        RegisterPlugin(PluginDescriptorValidator.Freeze(manifest.manifest.plugin),
            () => throw new InvalidOperationException("Manifest extensions require their activated session host facilities."),
            settings, pathBase ?? System.IO.Path.GetDirectoryName(manifest.path), workerOnly);
        string id = manifest.manifest.plugin.id;
        m_plugins[id] = m_plugins[id] with
        {
            Manifest = manifest,
            Factory = context => workerOnly
            ? throw new InvalidOperationException("A worker-only plugin cannot be activated in the SDK Host.")
            : new PluginLoader(context.RequireHostServices().paths).Activate(manifest)
        };
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

    private bool Registered(string id) => m_services.ContainsKey(id) || m_plugins.ContainsKey(id)
        || m_plugins.Values.Any(plugin => plugin.Descriptor.services.Any(service => service.id == id));

    /// <summary>Creates an immutable composition without discovering or selecting any platform implementation.</summary>
    /// <returns>The caller-owned host; missing capabilities fail when a session resolves its explicit inputs.</returns>
    public BindingHost Build() => new(Freeze());

    internal FrozenExtensionRegistry Freeze()
    {
        ImmutableArray<ServiceRegistration> services = m_services.Values
            .OrderByDescending(value => value.Descriptor.priority)
            .ThenBy(value => value.Descriptor.id, StringComparer.Ordinal).ToImmutableArray();
        ImmutableArray<PluginRegistration> plugins = m_plugins.Values.OrderBy(value => value.Descriptor.id, StringComparer.Ordinal).ToImmutableArray();
        foreach (ExtensionPort port in new[] { ExtensionPort.Frontend, ExtensionPort.ManagedProjection,
            ExtensionPort.BindingBackend, ExtensionPort.HostServicesProvider })
            if (services.Count(value => value.Descriptor.port == port)
                + plugins.Sum(plugin => plugin.Descriptor.services.Count(service => service.port == port)) > 1)
                throw new InvalidOperationException($"More than one primary '{port}' service is registered.");
        return new(services, plugins)
        {
            WorkerPlugins = plugins.Where(plugin => plugin.Manifest?.manifest.workerOnly == true).ToImmutableArray()
        };
    }

    internal static BindingHostBuilder FromFrozen(
        FrozenExtensionRegistry registry,
        bool includePlugins = false
    ) {
        BindingHostBuilder builder = new();
        foreach (ServiceRegistration service in registry.Services) builder.m_services.Add(service.Descriptor.id, service);
        if (includePlugins)
            foreach (PluginRegistration plugin in registry.Plugins) builder.m_plugins.Add(plugin.Descriptor.id, plugin);
        return builder;
    }
}
