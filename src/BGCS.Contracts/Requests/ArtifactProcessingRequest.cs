using System.Collections.Immutable;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Managed;

namespace BGCS.Contracts.Requests;

/// <summary>Captures checked API semantics and an exact editable candidate source set.</summary>
/// <param name="api">The frozen managed projection and underlying ABI.</param>
/// <param name="artifacts">The candidate descriptors at the start of this processor.</param>
/// <param name="content">The owner-controlled read and replacement surface.</param>
public sealed record ArtifactProcessingRequest(
    ManagedApiModel api,
    ImmutableArray<ArtifactDescriptor> artifacts,
    IArtifactEditor content
);
