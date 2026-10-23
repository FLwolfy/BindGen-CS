using BGCS.Core.Common;

namespace BGCS.Core.Artifacts;

/// <summary>Identifies a completed logical artifact without granting filesystem ownership.</summary>
/// <param name="relativePath">The portable candidate-relative path.</param>
/// <param name="mediaType">The artifact media type.</param>
/// <param name="digest">The exact artifact content identity.</param>
public sealed record ArtifactDescriptor(
    string relativePath,
    string mediaType,
    ContentDigest digest
);
