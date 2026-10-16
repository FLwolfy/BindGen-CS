using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using BGCS.Contracts.Artifacts;
using BGCS.Infrastructure.IO;
using BGCS.Infrastructure.Verification;

namespace BGCS.Infrastructure.Publishing;

/// <summary>Reads a complete owned output while cooperating with the publication lock.</summary>
public sealed class ArtifactPublicationReader
{
    /// <summary>Verifies the complete previous artifact set without altering its files or ownership.</summary>
    /// <param name="outputRoot">The formal output to inspect.</param>
    /// <param name="libraryId">The expected logical owner.</param>
    /// <param name="cancellationToken">Cancels waiting for an active publisher.</param>
    /// <returns>The immutable verified artifact set, or empty when no output exists.</returns>
    public ImmutableArray<ArtifactDescriptor> ReadVerified(
        string outputRoot,
        string libraryId,
        CancellationToken cancellationToken = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryId);
        OwnedPathBoundary boundary = new(outputRoot);
        string absolute = boundary.physicalPath;
        if (!Directory.Exists(absolute)) { boundary.Verify(); return []; }
        using FileStream publicationLock = DirectoryPublicationLease.Acquire(absolute, TimeSpan.FromSeconds(30), cancellationToken);
        boundary.Verify();
        if (!Directory.Exists(absolute)) { boundary.Verify(); return []; }
        if (!Directory.EnumerateFileSystemEntries(absolute).Any()) { boundary.Verify(); return []; }
        string manifestPath = Path.Combine(absolute, ".bgcs-artifacts.json");
        ArtifactDigestVerifier.RejectLinkedAncestors(manifestPath);
        if (!File.Exists(manifestPath)) throw new IOException("The existing output has no BGCS artifact ownership manifest.");
        PublicationOwnership owner = PublicationJournalStorage.Read<PublicationOwnership>(manifestPath);
        if (owner.schemaVersion != 1 || owner.libraryId != libraryId || owner.artifacts.IsDefault)
            throw new IOException("The existing output does not match the expected BGCS publication owner.");
        cancellationToken.ThrowIfCancellationRequested();
        new ArtifactDigestVerifier().Verify(absolute, owner.artifacts, [".bgcs-artifacts.json"]);
        boundary.Verify();
        return owner.artifacts;
    }
}
