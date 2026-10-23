using System.Collections.Immutable;
using System.Text.Json;

namespace BGCS.Core.Plugins;

/// <summary>Identifies a neutral extension port without exposing an implementation assembly.</summary>
public enum ExtensionPort
{
    /// <summary>The primary detached native parser.</summary>
    Frontend,
    /// <summary>A native target and ABI family provider.</summary>
    NativeTargetProvider,
    /// <summary>A detached native-fact transformation.</summary>
    NativeModuleTransform,
    /// <summary>A typed C++ value representation lowering.</summary>
    NativeTypeLowering,
    /// <summary>A typed C++ callable operation lowering.</summary>
    NativeCallableLowering,
    /// <summary>A final C ABI semantic transformation.</summary>
    InteropTransform,
    /// <summary>The primary managed semantic projection.</summary>
    ManagedProjection,
    /// <summary>The primary source artifact backend.</summary>
    BindingBackend,
    /// <summary>A final candidate validation gate.</summary>
    GenerationValidator,
    /// <summary>A candidate source artifact processor.</summary>
    ArtifactProcessor,
    /// <summary>Immutable generated source inputs captured before the original parse and affected cache lookup.</summary>
    SourceContributor = 12,
    /// <summary>A composable managed presentation change after the primary projection, preserving native ABI facts.</summary>
    ManagedApiTransform = 13,
    /// <summary>A parser-specific live fact contribution activated exclusively inside its matching worker.</summary>
    FrontendFactContributor = 14,
    /// <summary>Explicit host path and process facilities.</summary>
    HostServicesProvider,
    /// <summary>An open managed execution profile.</summary>
    ManagedRuntimeProvider,
    /// <summary>A target and runtime bound call classification provider.</summary>
    CallAbiProvider
}

/// <summary>Declares a service identity and frozen matching order before session activation.</summary>
/// <param name="id">The globally unique service identity.</param>
/// <param name="port">The neutral service contract.</param>
/// <param name="priority">Descending matching or execution priority.</param>
public sealed record ExtensionDescriptor(
    string id,
    ExtensionPort port,
    int priority = 0
);

/// <summary>Declares immutable plugin compatibility and the exact services it will contribute.</summary>
/// <param name="id">The globally unique plugin identity.</param>
/// <param name="version">The implementation's semantic version.</param>
/// <param name="services">The complete immutable service descriptor collection.</param>
public sealed record PluginDescriptor(
    string id,
    string version,
    ImmutableArray<ExtensionDescriptor> services
)
{
    /// <summary>Gets the required Contracts major release.</summary>
    public int contractsMajor { get; init; } = BGCS.Core.Versioning.ContractVersion.C_MAJOR;
    /// <summary>Gets the minimum Contracts API minor revision.</summary>
    public int minimumApiMinor { get; init; } = BGCS.Core.Versioning.ContractVersion.C_API_MINOR;
    /// <summary>Gets a bundled Draft 2020-12 schema for the plugin's own settings namespace.</summary>
    public JsonElement configurationSchema { get; init; } = JsonSerializer.SerializeToElement(new { type = "object", additionalProperties = false });
}
