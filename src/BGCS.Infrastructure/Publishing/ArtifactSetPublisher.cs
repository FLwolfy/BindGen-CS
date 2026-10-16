using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Infrastructure.IO;
using BGCS.Infrastructure.Verification;

namespace BGCS.Infrastructure.Publishing;

/// <summary>Names an explicitly owned publication directory or one exactly named native file.</summary>
/// <param name="path">The absolute formal destination.</param>
/// <param name="libraryId">The stable publication owner.</param>
/// <param name="isDirectory">True for a complete artifact directory; false for one file plus its ownership sidecar.</param>
public sealed record ArtifactSetDestination(
    string path,
    string libraryId,
    bool isDirectory = true
);

/// <summary>Journals a set of file and directory replacements while retaining every previous output until all installs verify.</summary>
/// <remarks>Readers must cooperate with the same publication locks. Multiple filesystems do not provide a simultaneous native rename.</remarks>
public sealed class ArtifactSetPublisher : IDisposable
{
    private const string C_OWNER = ".bgcs-artifacts.json";
    private const string C_FILEOWNER = ".bgcs-artifact.json";
    private readonly ImmutableArray<ArtifactSetDestination> m_destinations;
    private readonly Dictionary<string, string> m_candidates;
    private readonly List<FileStream> m_locks = [];
    private readonly string m_journal;
    private bool m_committed;
    private bool m_disposed;
    /// <summary>Acquires every destination lock in stable order, recovers interrupted work and validates existing owners.</summary>
    /// <param name="destinations">The complete nonoverlapping publication set.</param>
    /// <param name="protectedInputs">Exact source/tool files or directories that publication cannot replace.</param>
    /// <param name="cancellationToken">Cancels lock acquisition before mutation.</param>
    public ArtifactSetPublisher(
        IEnumerable<ArtifactSetDestination> destinations,
        IEnumerable<string>? protectedInputs = null,
        CancellationToken cancellationToken = default
    ) {
        ArgumentNullException.ThrowIfNull(destinations);
        StringComparer comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        m_destinations = destinations.Select(destination => destination with { path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination.path)) })
            .OrderBy(destination => DirectoryPublicationLease.GetPath(destination.path), comparer).ToImmutableArray();
        if (m_destinations.IsEmpty) throw new ArgumentException("A publication set requires at least one destination.", nameof(destinations));
        m_candidates = new(comparer);
        for (int index = 0; index < m_destinations.Length; index++)
        {
            ArtifactSetDestination destination = m_destinations[index];
            ArgumentException.ThrowIfNullOrWhiteSpace(destination.libraryId);
            if (Path.GetDirectoryName(destination.path) is null) throw new IOException("A filesystem root cannot be an artifact destination.");
            RejectLinks(destination.path);
            foreach (ArtifactSetDestination other in m_destinations.Take(index))
                if (Overlaps(destination.path, other.path)) throw new ArgumentException("Artifact publication destinations overlap.", nameof(destinations));
            foreach (string input in protectedInputs ?? [])
                if (Overlaps(destination.path, Path.GetFullPath(input))) throw new IOException("Artifact publication overlaps a protected native input: " + input);
        }
        string identity = string.Join('\n', m_destinations.Select(destination => (OperatingSystem.IsWindows() ? destination.path.ToUpperInvariant() : destination.path)
            + "\n" + destination.libraryId + "\n" + destination.isDirectory));
        m_journal = Path.Combine(Path.GetDirectoryName(m_destinations[0].path)!, ".bgcs-set-publication-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity))) + ".json");
        try
        {
            foreach (ArtifactSetDestination destination in m_destinations) m_locks.Add(DirectoryPublicationLease.Acquire(destination.path, TimeSpan.FromMinutes(5), cancellationToken));
            Recover();
            foreach (ArtifactSetDestination destination in m_destinations)
            {
                ReadExisting(destination);
                string candidate = Temporary(destination.path, "candidate");
                if (destination.isDirectory) Directory.CreateDirectory(candidate);
                m_candidates.Add(destination.path, candidate);
            }
        }
        catch { Dispose(); throw; }
    }
    /// <summary>Gets the private candidate path for an exactly named formal destination.</summary>
    /// <param name="destination">The complete formal file or directory destination.</param>
    /// <returns>The private unpublished candidate file or directory.</returns>
    public string GetCandidatePath(string destination)
        => m_candidates.TryGetValue(Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination)), out string? candidate) ? candidate : throw new ArgumentException("The destination is outside this publication set.", nameof(destination));
    /// <summary>Reads existing verified artifact identities while this set retains their publication locks.</summary>
    /// <param name="destination">The exactly named formal destination.</param>
    /// <returns>The complete existing artifact collection, or empty for absent output.</returns>
    public ImmutableArray<ArtifactDescriptor> ReadExistingArtifacts(string destination)
        => ReadExisting(m_destinations.Single(item => Equal(item.path, Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination)))));
    /// <summary>Gets a cleanup error after complete publication; retained journal recovery retries cleanup.</summary>
    public Exception? cleanupFailure { get; private set; }
    /// <summary>Verifies all candidates, journals their full identities and installs the set with rollback on failure.</summary>
    /// <param name="artifacts">Complete artifact collections keyed by exact destination path.</param>
    /// <param name="cancellationToken">Cancels before journaled installation starts.</param>
    public void Commit(
        IReadOnlyDictionary<string, ImmutableArray<ArtifactDescriptor>> artifacts,
        CancellationToken cancellationToken = default
    ) {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (m_committed) throw new InvalidOperationException("An artifact set cannot commit twice.");
        ArgumentNullException.ThrowIfNull(artifacts);
        List<SetEntry> entries = [];
        foreach (ArtifactSetDestination destination in m_destinations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!artifacts.TryGetValue(destination.path, out ImmutableArray<ArtifactDescriptor> expected) || expected.IsDefaultOrEmpty) throw new IOException("A publication destination has no complete artifact manifest.");
            string candidate = m_candidates[destination.path];
            ImmutableArray<ArtifactDescriptor> previous = ReadExisting(destination);
            Verify(candidate, destination, expected, ownerRequired: false);
            WriteOwner(candidate, destination, expected);
            Verify(candidate, destination, expected, ownerRequired: true);
            entries.Add(new(destination, candidate, Exists(destination.path, destination.isDirectory) ? Temporary(destination.path, "backup") : null, previous, expected));
        }
        SetJournal journal = new(1, "prepared", entries.ToImmutableArray());
        cancellationToken.ThrowIfCancellationRequested();
        PublicationJournalStorage.Write(m_journal, journal);
        try
        {
            foreach (SetEntry entry in journal.entries)
            {
                if (entry.backup is not null) Move(entry.destination.path, entry.backup, entry.destination.isDirectory);
                Move(entry.candidate, entry.destination.path, entry.destination.isDirectory);
                Verify(entry.destination.path, entry.destination, entry.current, ownerRequired: true);
            }
            PublicationJournalStorage.Write(m_journal, journal with { state = "committed" });
            m_committed = true;
        }
        catch (Exception failure)
        {
            try { Recover(); }
            catch (Exception rollback) { throw new AggregateException("Artifact set installation failed and recovery requires the retained journal.", failure, rollback); }
            throw;
        }
        try { Recover(); }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { cleanupFailure = failure; }
    }
    /// <summary>Discards private unjournaled candidates and releases all retained locks.</summary>
    public void Dispose()
    {
        if (m_disposed) return;
        m_disposed = true;
        try
        {
            if (!File.Exists(m_journal))
                foreach (ArtifactSetDestination destination in m_destinations)
                    if (m_candidates.TryGetValue(destination.path, out string? candidate)) DeletePrivate(candidate, destination, "candidate");
        }
        finally { for (int index = m_locks.Count - 1; index >= 0; index--) m_locks[index].Dispose(); }
    }
    private ImmutableArray<ArtifactDescriptor> ReadExisting(ArtifactSetDestination destination)
    {
        if (destination.isDirectory)
        {
            if (File.Exists(destination.path)) throw new IOException("An artifact directory destination is an existing file.");
            if (!Directory.Exists(destination.path) || !Directory.EnumerateFileSystemEntries(destination.path).Any()) return [];
            PublicationOwnership owner = PublicationJournalStorage.Read<PublicationOwnership>(Path.Combine(destination.path, C_OWNER));
            if (owner.schemaVersion != 1 || owner.libraryId != destination.libraryId || owner.artifacts.IsDefault) throw new IOException("An artifact directory belongs to another publication owner.");
            Verify(destination.path, destination, owner.artifacts, ownerRequired: true);
            return owner.artifacts;
        }
        if (Directory.Exists(destination.path)) throw new IOException("An artifact file destination is an existing directory.");
        if (!File.Exists(destination.path))
        {
            if (File.Exists(destination.path + C_FILEOWNER)) throw new IOException("An orphaned native file ownership sidecar requires inspection.");
            return [];
        }
        FileOwner fileOwner = PublicationJournalStorage.Read<FileOwner>(destination.path + C_FILEOWNER);
        if (fileOwner.schemaVersion != 1 || fileOwner.libraryId != destination.libraryId || !Equal(fileOwner.destination, destination.path)) throw new IOException("An existing native output file has no matching BGCS ownership.");
        ImmutableArray<ArtifactDescriptor> result = [new(Path.GetFileName(destination.path), "application/octet-stream", fileOwner.digest)];
        Verify(destination.path, destination, result, ownerRequired: true);
        return result;
    }
    private void Recover()
    {
        RejectLinks(m_journal);
        string temporaryJournal = m_journal + ".tmp";
        RejectLinks(temporaryJournal);
        if (File.Exists(temporaryJournal))
        {
            SetJournal temporary = PublicationJournalStorage.Read<SetJournal>(temporaryJournal);
            ValidateJournal(temporary);
            if (!File.Exists(m_journal)) File.Move(temporaryJournal, m_journal);
            else File.Delete(temporaryJournal);
        }
        if (!File.Exists(m_journal)) return;
        SetJournal journal = PublicationJournalStorage.Read<SetJournal>(m_journal);
        ValidateJournal(journal);
        if (journal.state == "committed")
        {
            foreach (SetEntry entry in journal.entries) Verify(entry.destination.path, entry.destination, entry.current, ownerRequired: true);
        }
        else
        {
            foreach (SetEntry entry in journal.entries.Reverse())
            {
                bool backup = entry.backup is not null && Exists(entry.backup, entry.destination.isDirectory);
                if (backup)
                {
                    Verify(entry.backup!, entry.destination, entry.previous, ownerRequired: false);
                    if (Exists(entry.destination.path, entry.destination.isDirectory))
                    {
                        Verify(entry.destination.path, entry.destination, entry.current, ownerRequired: false);
                        if (Exists(entry.candidate, entry.destination.isDirectory)) throw new IOException("Artifact recovery found ambiguous candidate and installed output.");
                        Move(entry.destination.path, entry.candidate, entry.destination.isDirectory);
                    }
                    Move(entry.backup!, entry.destination.path, entry.destination.isDirectory);
                    if (!entry.destination.isDirectory) WriteOwner(entry.destination.path, entry.destination, entry.previous);
                    Verify(entry.destination.path, entry.destination, entry.previous, ownerRequired: !entry.previous.IsEmpty);
                }
                else if (entry.previous.IsEmpty && Exists(entry.destination.path, entry.destination.isDirectory))
                {
                    Verify(entry.destination.path, entry.destination, entry.current, ownerRequired: false);
                    if (Exists(entry.candidate, entry.destination.isDirectory)) throw new IOException("Artifact recovery found ambiguous new output and candidate.");
                    Move(entry.destination.path, entry.candidate, entry.destination.isDirectory);
                }
                else if (!entry.previous.IsEmpty) Verify(entry.destination.path, entry.destination, entry.previous, ownerRequired: true);
            }
        }
        foreach (SetEntry entry in journal.entries)
        {
            if (entry.backup is not null) DeletePrivate(entry.backup, entry.destination, "backup");
            DeletePrivate(entry.candidate, entry.destination, "candidate");
        }
        File.Delete(m_journal);
    }
    private void ValidateJournal(SetJournal journal)
    {
        if (journal.schemaVersion != 1 || journal.state is not ("prepared" or "committed") || journal.entries.IsDefault || journal.entries.Length != m_destinations.Length)
            throw new IOException("The artifact set recovery journal is incompatible.");
        for (int index = 0; index < journal.entries.Length; index++)
        {
            SetEntry entry = journal.entries[index];
            ArtifactSetDestination expected = m_destinations[index];
            if (!Equal(entry.destination.path, expected.path) || entry.destination.libraryId != expected.libraryId || entry.destination.isDirectory != expected.isDirectory
                || entry.previous.IsDefault || entry.current.IsDefaultOrEmpty)
                throw new IOException("The artifact set recovery journal names a different or incomplete output set.");
            ValidatePrivate(entry.candidate, entry.destination, "candidate");
            if (entry.backup is not null) ValidatePrivate(entry.backup, entry.destination, "backup");
        }
    }
    private static void Verify(
        string path,
        ArtifactSetDestination destination,
        ImmutableArray<ArtifactDescriptor> expected,
        bool ownerRequired
    ) {
        RejectLinks(path);
        if (destination.isDirectory)
        {
            new ArtifactDigestVerifier().Verify(path, expected, ownerRequired || File.Exists(Path.Combine(path, C_OWNER)) ? [C_OWNER] : []);
            if (ownerRequired)
            {
                PublicationOwnership owner = PublicationJournalStorage.Read<PublicationOwnership>(Path.Combine(path, C_OWNER));
                if (owner.schemaVersion != 1 || owner.libraryId != destination.libraryId || !owner.artifacts.SequenceEqual(expected)) throw new IOException("The artifact directory ownership does not match its publication journal.");
            }
        }
        else
        {
            if (expected.Length != 1 || expected[0].relativePath != Path.GetFileName(destination.path) || !File.Exists(path)) throw new IOException("A native file publication must contain exactly its explicitly named file.");
            using FileStream file = File.OpenRead(path);
            if (Convert.ToHexStringLower(SHA256.HashData(file)) != expected[0].digest.value) throw new IOException("The native file content differs from the publication manifest.");
            if (ownerRequired)
            {
                RejectLinks(path + C_FILEOWNER);
                FileOwner owner = PublicationJournalStorage.Read<FileOwner>(path + C_FILEOWNER);
                if (owner.schemaVersion != 1 || owner.libraryId != destination.libraryId || !Equal(owner.destination, destination.path) || owner.digest != expected[0].digest) throw new IOException("The native file ownership differs from the publication manifest.");
            }
        }
    }
    private static void WriteOwner(
        string candidate,
        ArtifactSetDestination destination,
        ImmutableArray<ArtifactDescriptor> artifacts
    ) {
        if (destination.isDirectory) PublicationJournalStorage.Write(Path.Combine(candidate, C_OWNER), new PublicationOwnership(1, destination.libraryId, artifacts));
        else PublicationJournalStorage.Write(candidate + C_FILEOWNER, new FileOwner(1, destination.libraryId, destination.path, artifacts[0].digest));
    }
    private static void Move(
        string source,
        string destination,
        bool directory
    ) {
        RejectLinks(source); RejectLinks(destination);
        if (directory) Directory.Move(source, destination);
        else
        {
            File.Move(source, destination);
            if (File.Exists(source + C_FILEOWNER)) File.Move(source + C_FILEOWNER, destination + C_FILEOWNER, overwrite: true);
        }
    }
    private static string Temporary(
        string destination,
        string role
    ) => Path.Combine(Path.GetDirectoryName(destination)!, ".bgcs-set-" + role + "-" + Guid.NewGuid().ToString("N"));
    private static void ValidatePrivate(
        string path,
        ArtifactSetDestination destination,
        string role
    ) {
        string name = Path.GetFileName(path), prefix = ".bgcs-set-" + role + "-";
        if (!Equal(Path.GetDirectoryName(path)!, Path.GetDirectoryName(destination.path)!) || !name.StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParseExact(name[prefix.Length..], "N", out _))
            throw new IOException("An artifact recovery path is outside its exact private destination scope.");
        RejectLinks(path);
    }
    private static void DeletePrivate(
        string path,
        ArtifactSetDestination destination,
        string role
    ) {
        ValidatePrivate(path, destination, role);
        if (destination.isDirectory) { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        else { File.Delete(path); RejectLinks(path + C_FILEOWNER); File.Delete(path + C_FILEOWNER); }
    }
    private static void RejectLinks(string path)
    {
        ArtifactDigestVerifier.RejectLinkedAncestors(path);
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if (new FileInfo(current).LinkTarget is not null) throw new IOException("Artifact publication cannot follow a symbolic link.");
    }
    private static bool Exists(
        string path,
        bool directory
    ) => directory ? Directory.Exists(path) : File.Exists(path);
    private static bool Equal(
        string first,
        string second
    ) => string.Equals(first, second, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static bool Overlaps(
        string first,
        string second
    ) => Equal(first, second)
        || first.StartsWith(second.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
        || second.StartsWith(first.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private sealed record FileOwner(
        int schemaVersion,
        string libraryId,
        string destination,
        ContentDigest digest
    );
    private sealed record SetEntry(
        ArtifactSetDestination destination,
        string candidate,
        string? backup,
        ImmutableArray<ArtifactDescriptor> previous,
        ImmutableArray<ArtifactDescriptor> current
    );
    private sealed record SetJournal(
        int schemaVersion,
        string state,
        ImmutableArray<SetEntry> entries
    );
}
