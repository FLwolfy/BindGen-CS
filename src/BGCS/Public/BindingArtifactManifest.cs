using System.Collections.Immutable;
using System.Threading;
using BGCS.Contracts.Artifacts;
using BGCS.Infrastructure.Publishing;

namespace BGCS;

/// <summary>Exposes verified publication reads to integrations through the public SDK boundary.</summary>
public static class BindingArtifactManifest
{
    /// <summary>Reads exact artifact identities under the shared publication lock and verifies ownership and complete content hashes.</summary>
    /// <param name="outputRoot">The formal BGCS output directory.</param>
    /// <param name="libraryId">The expected logical library owner.</param>
    /// <param name="cancellationToken">Cancels waiting for publication ownership.</param>
    /// <returns>The verified complete artifact collection, or empty when no output exists.</returns>
    public static ImmutableArray<ArtifactDescriptor> Read(
        string outputRoot,
        string libraryId,
        CancellationToken cancellationToken = default
    )
        => new ArtifactPublicationReader().ReadVerified(outputRoot, libraryId, cancellationToken);
}
