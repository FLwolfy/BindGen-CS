using System;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Core.Hosting;
using BGCS.Core.Plugins;
using BGCS.Core.Ports;
using BGCS.Core.Targets;
using BGCS.Infrastructure.Plugins;

namespace BGCS.Composition;

internal sealed record ServiceRegistration(
    ExtensionDescriptor Descriptor,
    Func<ExtensionActivationContext, object> Factory
);

internal sealed record ServiceFactory<T>(
    ExtensionDescriptor Descriptor,
    Func<T> Factory
) where T : class
{
    internal string Id => Descriptor.id;
    internal int Priority => Descriptor.priority;
}

internal sealed record FrozenExtensionRegistry(
    ImmutableArray<ServiceRegistration> Services,
    ImmutableArray<PluginRegistration> Plugins
)
{
    internal ExtensionActivationContext Activation { get; init; } = new(null, string.Empty);
    internal SessionServiceScope? Scope { get; init; }
    internal ImmutableArray<PluginRegistration> WorkerPlugins { get; init; } = [];
    internal Func<IFrontend> Frontend => () => Required<IFrontend>(ExtensionPort.Frontend);
    internal IFrontend CreateVerificationFrontend()
    {
        ServiceRegistration[] entries = Services.Where(service => service.Descriptor.port == ExtensionPort.Frontend).ToArray();
        if (entries.Length != 1) throw new InvalidOperationException("Input verification requires the same unique frozen frontend factory.");
        return (IFrontend)entries[0].Factory(Activation);
    }
    internal Func<IHostServicesProvider> Host => () => Required<IHostServicesProvider>(ExtensionPort.HostServicesProvider);
    internal Func<IManagedProjection> Projection => () => Optional<IManagedProjection>(ExtensionPort.ManagedProjection)
        ?? Own(new BGCS.CSharp.Projection.DefaultManagedProjection());
    internal Func<IBindingBackend> Backend => () => Optional<IBindingBackend>(ExtensionPort.BindingBackend)
        ?? Own(new BGCS.CSharp.CSharpBackend());
    internal ImmutableArray<ServiceFactory<INativeTargetProvider>> Targets => For<INativeTargetProvider>(ExtensionPort.NativeTargetProvider);
    internal ImmutableArray<ServiceFactory<IManagedRuntimeProvider>> Runtimes => For<IManagedRuntimeProvider>(ExtensionPort.ManagedRuntimeProvider);
    internal ImmutableArray<ServiceFactory<ICallAbiProvider>> CallAbis => For<ICallAbiProvider>(ExtensionPort.CallAbiProvider);
    internal ImmutableArray<ServiceFactory<INativeModuleTransform>> NativeTransforms => For<INativeModuleTransform>(ExtensionPort.NativeModuleTransform);
    internal ImmutableArray<ServiceFactory<IInteropTransform>> InteropTransforms => For<IInteropTransform>(ExtensionPort.InteropTransform);
    internal ImmutableArray<ServiceFactory<IManagedApiTransform>> ManagedTransforms => For<IManagedApiTransform>(ExtensionPort.ManagedApiTransform);
    internal ImmutableArray<ServiceFactory<INativeTypeLowering>> NativeTypes => For<INativeTypeLowering>(ExtensionPort.NativeTypeLowering);
    internal ImmutableArray<ServiceFactory<INativeCallableLowering>> NativeCallables => For<INativeCallableLowering>(ExtensionPort.NativeCallableLowering);
    internal ImmutableArray<ServiceFactory<IGenerationValidator>> Validators => For<IGenerationValidator>(ExtensionPort.GenerationValidator);
    internal ImmutableArray<ServiceFactory<IArtifactProcessor>> Processors => For<IArtifactProcessor>(ExtensionPort.ArtifactProcessor);
    internal ImmutableArray<ServiceFactory<ISourceContributor>> SourceContributors => For<ISourceContributor>(ExtensionPort.SourceContributor);

    private ImmutableArray<ServiceFactory<T>> For<T>(ExtensionPort port) where T : class => Services
        .Where(service => service.Descriptor.port == port)
        .Select(service => new ServiceFactory<T>(service.Descriptor, () => Own((T)service.Factory(Activation)))).ToImmutableArray();

    private T? Optional<T>(ExtensionPort port) where T : class
    {
        ServiceRegistration[] selected = Services.Where(service => service.Descriptor.port == port).ToArray();
        if (selected.Length > 1) throw new InvalidOperationException($"Multiple primary services claim '{port}'.");
        return selected.Length == 0 ? null : Own((T)selected[0].Factory(Activation));
    }

    private T Required<T>(ExtensionPort port) where T : class => Optional<T>(port)
        ?? throw new InvalidOperationException($"The explicit composition has no '{port}' capability. Register its extension before execution.");

    private T Own<T>(T value) where T : class => Scope?.Own(value) ?? value;
}

internal sealed record PluginRegistration(
    PluginDescriptor Descriptor,
    Func<ExtensionActivationContext, IBindingPlugin> Factory
)
{
    internal LockedPluginManifest? Manifest { get; init; }
    internal PluginConfiguration? Configuration { get; init; }
}

internal static class ExtensionPortResolver
{
    internal static ExtensionPort Resolve(Type contract)
    {
        if (contract == typeof(IFrontend)) return ExtensionPort.Frontend;
        if (contract == typeof(IHostServicesProvider)) return ExtensionPort.HostServicesProvider;
        if (contract == typeof(INativeTargetProvider)) return ExtensionPort.NativeTargetProvider;
        if (contract == typeof(IManagedRuntimeProvider)) return ExtensionPort.ManagedRuntimeProvider;
        if (contract == typeof(ICallAbiProvider)) return ExtensionPort.CallAbiProvider;
        if (contract == typeof(INativeModuleTransform)) return ExtensionPort.NativeModuleTransform;
        if (contract == typeof(IInteropTransform)) return ExtensionPort.InteropTransform;
        if (contract == typeof(IManagedApiTransform)) return ExtensionPort.ManagedApiTransform;
        if (contract == typeof(INativeTypeLowering)) return ExtensionPort.NativeTypeLowering;
        if (contract == typeof(INativeCallableLowering)) return ExtensionPort.NativeCallableLowering;
        if (contract == typeof(IManagedProjection)) return ExtensionPort.ManagedProjection;
        if (contract == typeof(IBindingBackend)) return ExtensionPort.BindingBackend;
        if (contract == typeof(IGenerationValidator)) return ExtensionPort.GenerationValidator;
        if (contract == typeof(IArtifactProcessor)) return ExtensionPort.ArtifactProcessor;
        if (contract == typeof(ISourceContributor)) return ExtensionPort.SourceContributor;
        throw new InvalidOperationException("Only exact neutral extension ports may be registered.");
    }
}
