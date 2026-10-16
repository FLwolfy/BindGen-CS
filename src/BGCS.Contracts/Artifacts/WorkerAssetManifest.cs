using System.Collections.Immutable;

namespace BGCS.Contracts.Artifacts;

/// <summary>Identifies a complete parser worker execution closure for one generator host.</summary>
/// <param name="schemaVersion">The worker asset manifest revision.</param>
/// <param name="hostRid">The generator host RID, independently of the native generation target.</param>
/// <param name="parserIdentity">The pinned parser and resource identity.</param>
/// <param name="artifacts">Every managed entry, dependency manifest and host-native file.</param>
public sealed record WorkerAssetManifest(
    int schemaVersion,
    string hostRid,
    string parserIdentity,
    ImmutableArray<ArtifactDescriptor> artifacts
);
