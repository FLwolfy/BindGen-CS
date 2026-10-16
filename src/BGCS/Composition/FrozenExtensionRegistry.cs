using System;
using System.Collections.Immutable;
using BGCS.Contracts.Plugins;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Targets;
using BGCS.Contracts.Inputs;
using BGCS.Infrastructure.Plugins;

namespace BGCS.Composition;

internal sealed record FrozenExtensionRegistry(
    Func<IFrontend> Frontend,
    bool FrontendConfigured,
    Func<IManagedProjection> Projection,
    bool ProjectionConfigured,
    Func<IBindingBackend> Backend,
    bool BackendConfigured,
    ImmutableArray<BindingHost.NativeTransformRegistration> NativeTransforms,
    ImmutableArray<BindingHost.InteropTransformRegistration> InteropTransforms,
    ImmutableArray<BindingHost.CppTypeRegistration> CppTypes,
    ImmutableArray<BindingHost.CppCallableRegistration> CppCallables,
    ImmutableArray<ValidatorRegistration> Validators,
    ImmutableArray<ProcessorRegistration> Processors,
    ImmutableArray<TargetProviderRegistration> Targets,
    ImmutableArray<NativeProviderRegistration> NativeBuildProviders,
    ImmutableArray<NativePackagerRegistration> NativeArtifactPackagers,
    ImmutableArray<SourceContributorRegistration> SourceContributors,
    ImmutableArray<PluginRegistration> Plugins
)
{
    internal TimeSpan ParseTimeout { get; init; } = TimeSpan.FromSeconds(300);
    internal ImmutableArray<BindingHost.ManagedTransformRegistration> ManagedTransforms { get; init; } = [];
    internal ImmutableArray<PluginRegistration> WorkerPlugins { get; init; } = [];
}

internal sealed record TargetProviderRegistration(
    string Id,
    Func<INativeTargetProvider> Factory
);
internal sealed record ValidatorRegistration(
    string Id,
    int Priority,
    Func<IGenerationValidator> Factory
);
internal sealed record ProcessorRegistration(
    string Id,
    int Priority,
    Func<IArtifactProcessor> Factory
);
internal sealed record NativeProviderRegistration(
    string Id,
    Func<INativeBuildProvider> Factory
);
internal sealed record NativePackagerRegistration(
    string Id,
    Func<INativeArtifactPackager> Factory
);
internal sealed record SourceContributorRegistration(
    string Id,
    int Priority,
    Func<ISourceContributor> Factory
);
internal sealed record PluginRegistration(
    PluginDescriptor Descriptor,
    Func<IBindingPlugin> Factory
)
{
    internal LockedPluginManifest? Manifest { get; init; }
    internal PluginConfiguration? Configuration { get; init; }
}
