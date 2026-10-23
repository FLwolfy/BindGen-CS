using System.Collections.Immutable;
using BGCS.Core.Calls;
using BGCS.Core.Common;
using BGCS.Core.Targets;

namespace BGCS.Core.Capabilities;

/// <summary>Reports source-generation capability for one declaration and one explicitly selected execution profile.</summary>
/// <param name="declarationId">The stable source declaration or physical boundary identity.</param>
/// <param name="nativeName">The diagnostic native spelling.</param>
/// <param name="nativeTargetId">The exact native target.</param>
/// <param name="runtimeId">The selected managed execution identity, or null when only native source was requested.</param>
/// <param name="runtimeProfileId">The exact execution profile identity, or null for native-source-only generation.</param>
/// <param name="status">The source-generation result established by the producing stage.</param>
/// <param name="reasons">Explicit limitations or omitted optional projections.</param>
/// <remarks>This record does not assert consumer compilation, native linking, invocation or verified ABI evidence.</remarks>
public sealed record BindingCapabilityReport(
    StableId declarationId,
    string nativeName,
    string nativeTargetId,
    ManagedRuntimeId? runtimeId,
    string? runtimeProfileId,
    DeclarationGenerationStatus status,
    ImmutableArray<string> reasons
)
{
    /// <summary>Gets the detached original source position when one is available.</summary>
    public SourceLocation? source { get; init; }
    /// <summary>Gets declarative consumer work required to use the generated source, without execution commands.</summary>
    public ImmutableArray<string> consumerRequirements { get; init; } = [];
    /// <summary>Gets the selected calling implementation without assigning verified status to it.</summary>
    public CallAbiId? callAbi { get; init; }
    /// <summary>Gets the execution-capability digest captured by the source-generation stage.</summary>
    public ContentDigest? runtimeProfileDigest { get; init; }
}
