using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BGCS.Contracts.Artifacts;
using BGCS.Infrastructure.IO;
using BGCS.Infrastructure.Verification;

namespace BGCS.Infrastructure.Publishing;

/// <summary>Owns one candidate set, enforces output ownership and journals recoverable directory replacement.</summary>
public sealed class ArtifactPublisher : IDisposable
{
    private const string C_OWNERSHIPFILE = ".bgcs-artifacts.json";
    private readonly OutputDirectoryTransaction m_transaction;
    private readonly string m_destination;
    private readonly string m_libraryId;
    private readonly string m_journalPath;
    private readonly ArtifactDigestVerifier m_verifier = new();
    private bool m_committed;
    private bool m_disposed;

    /// <summary>Locks the destination, recovers interrupted publication and prepares an independent candidate.</summary>
    /// <param name="destination">The explicitly owned output directory.</param>
    /// <param name="libraryId">The stable logical library owner.</param>
    /// <param name="protectedInputs">Source, cache and other roots that may not overlap this output.</param>
    /// <param name="cancellationToken">Cancels waiting for publication ownership.</param>
    /// <exception cref="IOException">Ownership, paths, previous artifacts or recovery records are invalid.</exception>
    public ArtifactPublisher(
        string destination,
        string libraryId,
        IEnumerable<string>? protectedInputs = null,
        CancellationToken cancellationToken = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryId);
        m_destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        m_libraryId = libraryId;
        string parent = Path.GetDirectoryName(m_destination) ?? throw new IOException("A filesystem root cannot be a BGCS output directory.");
        ArtifactDigestVerifier.RejectLinkedAncestors(m_destination);
        foreach (string input in protectedInputs ?? [])
            if (Overlap(m_destination, Path.GetFullPath(input))) throw new IOException($"Output overlaps protected input '{input}'.");
        string identity = OperatingSystem.IsWindows() ? m_destination.ToUpperInvariant() : m_destination;
        m_journalPath = Path.Combine(parent, ".bgcs-publication-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity))) + ".json");
        m_transaction = new(m_destination, cancellationToken: cancellationToken);
        try
        {
            Recover();
            ValidatePrevious();
        }
        catch
        {
            m_transaction.Dispose();
            throw;
        }
    }

    /// <summary>Gets the private candidate root; no candidate file is a formal output before commit.</summary>
    public string candidateRoot => m_transaction.stagingPath;

    /// <summary>Gets a postcommit backup cleanup failure retained for the next journal recovery.</summary>
    public Exception? cleanupFailure { get; private set; }

    /// <summary>Clears a fully verified owned output under the same recovery and publication lock as generation.</summary>
    /// <param name="destination">The complete owned output directory.</param>
    /// <param name="libraryId">The expected stable output owner.</param>
    /// <param name="protectedInputs">Inputs that the output must not overlap.</param>
    /// <param name="cancellationToken">Cancels before the verified replacement begins.</param>
    /// <returns>False if the output is absent; true after its artifacts are removed and a valid empty ownership manifest remains.</returns>
    /// <exception cref="IOException">Ownership or content verification fails, or cleanup requires journal recovery.</exception>
    public static bool ClearOwned(
        string destination,
        string libraryId,
        IEnumerable<string>? protectedInputs = null,
        CancellationToken cancellationToken = default
    ) {
        using ArtifactPublisher publisher = new(destination, libraryId, protectedInputs, cancellationToken);
        if (!Directory.Exists(publisher.m_destination)) return false;
        if (!File.Exists(Path.Combine(publisher.m_destination, C_OWNERSHIPFILE)))
            throw new IOException("Clearing output requires an existing BGCS ownership manifest.");
        publisher.Commit([], cancellationToken);
        if (publisher.cleanupFailure is not null)
            throw new IOException("The owned output was cleared, but backup cleanup requires publication journal recovery.", publisher.cleanupFailure);
        return true;
    }

    /// <summary>Checks exact candidate digests, records ownership and installs the whole verified set.</summary>
    /// <param name="artifacts">The complete validated candidate artifact set.</param>
    /// <param name="cancellationToken">Cancels before directory replacement begins.</param>
    /// <remarks>A single filesystem directory is replaced under a publication lock; independent readers must follow the same manifest/lock protocol.</remarks>
    public void Commit(
        IEnumerable<ArtifactDescriptor> artifacts,
        CancellationToken cancellationToken = default
    ) {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (m_committed) throw new InvalidOperationException("Publication has already committed.");
        ArgumentNullException.ThrowIfNull(artifacts);
        ImmutableArray<ArtifactDescriptor> snapshot = artifacts.ToImmutableArray();
        m_verifier.Verify(candidateRoot, snapshot);
        ValidatePrevious();
        if (File.Exists(Path.Combine(m_destination, C_OWNERSHIPFILE)))
        {
            PublicationOwnership previous = PublicationJournalStorage.Read<PublicationOwnership>(Path.Combine(m_destination, C_OWNERSHIPFILE));
            if (previous.artifacts.OrderBy(artifact => artifact.relativePath, StringComparer.Ordinal)
                .SequenceEqual(snapshot.OrderBy(artifact => artifact.relativePath, StringComparer.Ordinal)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                m_committed = true;
                return;
            }
            var existing = previous.artifacts.ToDictionary(artifact => artifact.relativePath, StringComparer.Ordinal);
            foreach (ArtifactDescriptor artifact in snapshot)
                if (existing.TryGetValue(artifact.relativePath, out ArtifactDescriptor? old) && old.digest == artifact.digest)
                    File.SetLastWriteTimeUtc(Path.Combine(candidateRoot, artifact.relativePath), File.GetLastWriteTimeUtc(Path.Combine(m_destination, artifact.relativePath)));
        }
        PublicationOwnership owner = new(1, m_libraryId, snapshot);
        PublicationJournalStorage.Write(Path.Combine(candidateRoot, C_OWNERSHIPFILE), owner);
        m_verifier.Verify(candidateRoot, snapshot, [C_OWNERSHIPFILE]);
        cancellationToken.ThrowIfCancellationRequested();
        string? backup = Directory.Exists(m_destination) ? Path.Combine(Path.GetDirectoryName(m_destination)!, ".bgcs-backup-" + Guid.NewGuid().ToString("N")) : null;
        PublicationJournal journal = new(m_destination, candidateRoot, backup, m_libraryId, "prepared", snapshot);
        PublicationJournalStorage.Write(m_journalPath, journal);
        bool installed = false;
        try
        {
            m_transaction.InstallCandidate(backup, requireMissingDestination: backup is null);
            installed = true;
            m_verifier.Verify(m_destination, snapshot, [C_OWNERSHIPFILE]);
            PublicationJournalStorage.Write(m_journalPath, journal with { state = "committed" });
            m_committed = true;
        }
        catch (Exception failure)
        {
            try
            {
                if (installed) m_transaction.RestorePrevious(backup);
                else if (backup is not null && Directory.Exists(backup))
                    throw new IOException("The previous output remains in its journaled backup and requires recovery.");
                File.Delete(m_journalPath);
            }
            catch (Exception rollbackFailure)
            {
                throw new AggregateException("Candidate publication failed and its journal was preserved for recovery.", failure, rollbackFailure);
            }
            throw;
        }
        try
        {
            m_transaction.RemoveBackup(backup);
            File.Delete(m_journalPath);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            cleanupFailure = failure;
        }
    }

    /// <summary>Discards uncommitted candidates and releases the publication lock.</summary>
    public void Dispose()
    {
        if (m_disposed) return;
        m_disposed = true;
        m_transaction.Dispose();
    }

    private void ValidatePrevious()
    {
        if (File.Exists(m_destination)) throw new IOException("The output directory is an existing file.");
        if (!Directory.Exists(m_destination) || !Directory.EnumerateFileSystemEntries(m_destination).Any()) return;
        ArtifactDigestVerifier.RejectLinkedAncestors(m_destination);
        string path = Path.Combine(m_destination, C_OWNERSHIPFILE);
        ArtifactDigestVerifier.RejectLinkedAncestors(path);
        if (!File.Exists(path)) throw new IOException("A nonempty output directory has no BGCS ownership manifest.");
        PublicationOwnership owner = PublicationJournalStorage.Read<PublicationOwnership>(path);
        if (owner.schemaVersion != 1 || owner.libraryId != m_libraryId || owner.artifacts.IsDefault)
            throw new IOException("The output belongs to a different library or an unsupported manifest revision.");
        m_verifier.Verify(m_destination, owner.artifacts, [C_OWNERSHIPFILE]);
    }

    private void Recover()
    {
        ArtifactDigestVerifier.RejectLinkedAncestors(m_journalPath);
        string temporaryJournal = m_journalPath + ".tmp";
        ArtifactDigestVerifier.RejectLinkedAncestors(temporaryJournal);
        if (File.Exists(temporaryJournal))
        {
            PublicationJournal temporary = PublicationJournalStorage.Read<PublicationJournal>(temporaryJournal);
            ValidateJournal(temporary);
            if (!File.Exists(m_journalPath)) File.Move(temporaryJournal, m_journalPath);
            else File.Delete(temporaryJournal);
        }
        if (!File.Exists(m_journalPath)) return;
        PublicationJournal journal = PublicationJournalStorage.Read<PublicationJournal>(m_journalPath);
        ValidateJournal(journal);
        if (journal.state == "committed")
        {
            VerifyRecordedOwner(m_destination, journal.artifacts);
            m_verifier.Verify(m_destination, journal.artifacts, [C_OWNERSHIPFILE]);
            if (journal.backup is not null && Directory.Exists(journal.backup)) ValidateBackupOwner(journal.backup);
            DeleteTemporary(journal.backup);
            DeleteTemporary(journal.candidate);
        }
        else
        {
            if (journal.backup is not null && Directory.Exists(journal.backup))
            {
                ValidateBackupOwner(journal.backup);
                if (Directory.Exists(m_destination))
                {
                    if (Directory.Exists(journal.candidate)) throw new IOException("Publication recovery found an ambiguous candidate and destination.");
                    VerifyRecordedOwner(m_destination, journal.artifacts);
                    DirectoryPublication.Move(m_destination, journal.candidate);
                }
                DirectoryPublication.Move(journal.backup, m_destination);
            }
            else if (journal.backup is null && !Directory.Exists(journal.candidate) && Directory.Exists(m_destination))
            {
                VerifyRecordedOwner(m_destination, journal.artifacts);
                DirectoryPublication.Move(m_destination, journal.candidate);
            }
            DeleteTemporary(journal.candidate);
        }
        File.Delete(m_journalPath);
    }

    private void ValidateJournal(PublicationJournal journal)
    {
        if (!Equal(journal.destination, m_destination) || journal.libraryId != m_libraryId || journal.artifacts.IsDefault || journal.state is not ("prepared" or "committed"))
            throw new IOException("The interrupted publication journal does not match this output owner.");
        ValidateTemporary(journal.candidate, ".bgcs-staging-");
        if (journal.backup is not null) ValidateTemporary(journal.backup, ".bgcs-backup-");
    }

    private void VerifyRecordedOwner(
        string root,
        ImmutableArray<ArtifactDescriptor> expected
    ) {
        string path = Path.Combine(root, C_OWNERSHIPFILE);
        ArtifactDigestVerifier.RejectLinkedAncestors(path);
        PublicationOwnership owner = PublicationJournalStorage.Read<PublicationOwnership>(path);
        if (owner.schemaVersion != 1 || owner.libraryId != m_libraryId || owner.artifacts.IsDefault || !owner.artifacts.SequenceEqual(expected))
            throw new IOException("A recovery output does not match the journaled publication ownership manifest.");
    }

    private void ValidateBackupOwner(string root)
    {
        if (!Directory.EnumerateFileSystemEntries(root).Any()) return;
        string path = Path.Combine(root, C_OWNERSHIPFILE);
        ArtifactDigestVerifier.RejectLinkedAncestors(path);
        PublicationOwnership owner = PublicationJournalStorage.Read<PublicationOwnership>(path);
        if (owner.schemaVersion != 1 || owner.libraryId != m_libraryId || owner.artifacts.IsDefault)
            throw new IOException("A recovery backup belongs to a different output owner.");
        m_verifier.Verify(root, owner.artifacts, [C_OWNERSHIPFILE]);
    }

    private void ValidateTemporary(
        string path,
        string prefix
    ) {
        string resolved = Path.GetFullPath(path);
        string name = Path.GetFileName(resolved);
        if (!Equal(Path.GetDirectoryName(resolved)!, Path.GetDirectoryName(m_destination)!) || !name.StartsWith(prefix, StringComparison.Ordinal)
            || !Guid.TryParseExact(name[prefix.Length..], "N", out _) || Equal(resolved, candidateRoot))
            throw new IOException("A publication recovery path is outside its owned temporary directory family.");
        ArtifactDigestVerifier.RejectLinkedAncestors(resolved);
    }

    private static void DeleteTemporary(string? path)
    {
        if (path is null || !Directory.Exists(path)) return;
        ArtifactDigestVerifier.RejectLinkedAncestors(path);
        Directory.Delete(path, recursive: true);
    }

    private static bool Equal(
        string left,
        string right
    ) => string.Equals(Path.TrimEndingDirectorySeparator(left), Path.TrimEndingDirectorySeparator(right), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static bool Overlap(
        string left,
        string right
    ) => Equal(left, right) || Path.GetRelativePath(left, right).Split(Path.DirectorySeparatorChar)[0] != ".." && !Path.IsPathRooted(Path.GetRelativePath(left, right))
        || Path.GetRelativePath(right, left).Split(Path.DirectorySeparatorChar)[0] != ".." && !Path.IsPathRooted(Path.GetRelativePath(right, left));
}
