using System.Collections.Immutable;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Targets;

namespace BGCS.Contracts.Requests;

/// <summary>Captures the final native contract and candidate artifacts before formal publication.</summary>
/// <param name="target">The resolved native target and SDK identity.</param>
/// <param name="interop">The final immutable ABI and lifecycle contracts.</param>
/// <param name="artifacts">The exact candidate artifact set for this target.</param>
/// <param name="content">The owner-controlled artifact read surface.</param>
public sealed record ValidationRequest(
    NativeTargetDescriptor target,
    InteropModule interop,
    ImmutableArray<ArtifactDescriptor> artifacts,
    IArtifactReader content
);
