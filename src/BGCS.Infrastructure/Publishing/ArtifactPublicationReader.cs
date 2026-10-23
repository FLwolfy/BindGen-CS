using BGCS.Core.Hosting;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using BGCS.Core.Artifacts;
using BGCS.Infrastructure.IO;
using BGCS.Infrastructure.Verification;

namespace BGCS.Infrastructure.Publishing;

/// <summary>Reads a complete owned output while cooperating with the publication lock.</summary>
public sealed class ArtifactPublicationReader
{
    private readonly IHostPathService m_paths;
    /// <summary>Captures the explicitly selected host path service.</summary>
    /// <param name="paths">The host path identity service.</param>
    public ArtifactPublicationReader(IHostPathService paths) => m_paths = paths ?? throw new ArgumentNullException(nameof(paths));
    /// <summary>Verifies the complete previous artifact set without altering its files or ownership.</summary>
    /// <param name="outputRoot">The formal output to inspect.</param>
    /// <param name="libraryId">The expected logical owner.</param>
    /// <param name="cancellationToken">Cancels waiting for an active publisher.</param>
    /// <returns>The immutable verified artifact set, or empty when no output exists.</returns>
    public ImmutableArray<ArtifactDescriptor> ReadVerified(
        string outputRoot,
        string libraryId,
        CancellationToken cancellationToken = default
    ) => ReadVerified(outputRoot, libraryId, static (
        _,
        artifacts
    ) => artifacts, cancellationToken);

    /// <summary>Reads selected content while the complete publication remains verified and locked.</summary>
    /// <typeparam name="TResult">The detached reader result.</typeparam>
    /// <param name="outputRoot">The formal BGCS output directory.</param>
    /// <param name="libraryId">The expected publication owner.</param>
    /// <param name="reader">A synchronous reader of the verified physical root and artifact identities; it must not retain handles or mutate the publication.</param>
    /// <param name="cancellationToken">Cancels waiting for publication ownership.</param>
    /// <returns>The detached result read from one complete publication, including an empty set when no output exists.</returns>
    public TResult ReadVerified<TResult>(
        string outputRoot,
        string libraryId,
        Func<string, ImmutableArray<ArtifactDescriptor>, TResult> reader,
        CancellationToken cancellationToken = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryId);
        ArgumentNullException.ThrowIfNull(reader);
        OwnedPathBoundary boundary = new(outputRoot, m_paths);
        string absolute = boundary.physicalPath;
        if (!Directory.Exists(absolute)) { boundary.Verify(); return reader(absolute, []); }
        using FileStream publicationLock = DirectoryPublicationLease.Acquire(absolute, m_paths, TimeSpan.FromSeconds(30), cancellationToken);
        boundary.Verify();
        if (!Directory.Exists(absolute)) { boundary.Verify(); return reader(absolute, []); }
        if (!Directory.EnumerateFileSystemEntries(absolute).Any()) { boundary.Verify(); return reader(absolute, []); }
        string manifestPath = Path.Combine(absolute, ".bgcs-artifacts.json");
        ArtifactDigestVerifier.RejectLinkedAncestors(manifestPath);
        if (!File.Exists(manifestPath)) throw new IOException("The existing output has no BGCS artifact ownership manifest.");
        PublicationOwnership owner = PublicationJournalStorage.Read<PublicationOwnership>(manifestPath, m_paths);
        if (owner.schemaVersion != 1 || owner.libraryId != libraryId || owner.artifacts.IsDefault)
            throw new IOException("The existing output does not match the expected BGCS publication owner.");
        cancellationToken.ThrowIfCancellationRequested();
        new ArtifactDigestVerifier(m_paths).Verify(absolute, owner.artifacts, [".bgcs-artifacts.json"]);
        boundary.Verify();
        TResult result = reader(absolute, owner.artifacts);
        boundary.Verify();
        return result;
    }
}
