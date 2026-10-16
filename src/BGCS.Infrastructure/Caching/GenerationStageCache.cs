using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Infrastructure.IO;

namespace BGCS.Infrastructure.Caching;

/// <summary>Stores bounded, complete candidate artifact sets without publishing or validating a consumer build.</summary>
public sealed class GenerationStageCache
{
    private const int C_REVISION = 2;
    private readonly string m_root;
    private readonly OwnedPathBoundary m_boundary;
    private readonly long m_maximumBytes;
    private readonly int m_maximumEntryBytes;

    /// <summary>Gets the bounded serialized-entry budget shared by all reusable generation stages.</summary>
    public int maximumEntryBytes => m_maximumEntryBytes;

    /// <summary>Captures a rebuildable cache directory and its disk budgets.</summary>
    /// <param name="root">The absolute or relative cache directory, outside all source and output roots.</param>
    /// <param name="maximumBytes">The total stored-entry budget in bytes.</param>
    /// <param name="maximumEntryBytes">The maximum serialized entry size, at most 256 MiB.</param>
    public GenerationStageCache(
        string root,
        long maximumBytes = 536870912,
        int maximumEntryBytes = 67108864
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (maximumBytes <= 0 || maximumEntryBytes <= 0 || maximumEntryBytes > 268435456 || maximumBytes < maximumEntryBytes)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes), "Cache budgets must be positive and contain at least one bounded entry.");
        m_boundary = new(root);
        m_root = m_boundary.physicalPath;
        if (string.Equals(m_root, Path.GetPathRoot(m_root)?.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A cache directory cannot be a volume root.", nameof(root));
        m_maximumBytes = maximumBytes;
        m_maximumEntryBytes = maximumEntryBytes;
        RejectLinks();
    }

    /// <summary>Creates the verified cache root before source-directory membership is frozen.</summary>
    /// <remarks>The caller must exclude only this owned cache subtree from search membership and still reject actual native reads from it.</remarks>
    public void Prepare()
    {
        RejectLinks();
        Directory.CreateDirectory(m_root);
        RejectLinks();
    }

    /// <summary>Reads and independently verifies a complete entry before exposing any of its artifacts.</summary>
    /// <param name="key">The complete stage identity.</param>
    /// <param name="artifacts">Receives all verified artifacts, or an empty array on a miss.</param>
    /// <param name="missReason">Receives a corruption or size reason, or null for an ordinary absent entry.</param>
    /// <param name="cancellationToken">Cancels reading and artifact verification.</param>
    /// <returns>True only when every path, descriptor and byte digest is valid.</returns>
    public bool TryRead(
        ContentDigest key,
        out ImmutableArray<CachedStageArtifact> artifacts,
        out string? missReason,
        CancellationToken cancellationToken = default
    ) {
        artifacts = [];
        missReason = null;
        string path = EntryPath(key);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectLinks();
            RejectLink(path);
            if (!Directory.Exists(m_root)) return false;
            using FileStream cacheLease = AcquireCacheLease(cancellationToken);
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (stream.Length <= 0 || stream.Length > m_maximumEntryBytes) throw new InvalidDataException("The cached entry exceeds its declared size budget.");
            byte[] bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            Entry entry = JsonSerializer.Deserialize<Entry>(bytes) ?? throw new InvalidDataException("The cached entry is empty.");
            if (entry.revision != C_REVISION || entry.key != key.value || entry.artifacts is null || entry.artifacts.Length > 50000)
                throw new InvalidDataException("The cached entry identity or artifact set is invalid.");
            HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
            ImmutableArray<CachedStageArtifact>.Builder verified = ImmutableArray.CreateBuilder<CachedStageArtifact>();
            foreach (StoredArtifact artifact in entry.artifacts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (artifact is null) throw new InvalidDataException("A cached artifact descriptor is absent.");
                ValidatePath(artifact.path);
                if (!paths.Add(artifact.path) || string.IsNullOrWhiteSpace(artifact.mediaType) || artifact.content is null)
                    throw new InvalidDataException("Cached artifact paths or descriptors conflict.");
                ContentDigest digest = new(artifact.digest);
                if (Convert.ToHexStringLower(SHA256.HashData(artifact.content)) != digest.value)
                    throw new InvalidDataException("A cached artifact failed its content digest verification.");
                verified.Add(new(new(artifact.path, artifact.mediaType, digest), artifact.content));
            }
            if (entry.seal != Seal(entry.revision, entry.key, entry.artifacts))
                throw new InvalidDataException("The cached artifact set failed its complete descriptor seal verification.");
            RejectLinks();
            RejectLink(path);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            RejectLinks();
            RejectLink(path);
            artifacts = verified.ToImmutable();
            return true;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        catch (Exception failure) when (failure is JsonException or InvalidDataException or IOException or ArgumentException or UnauthorizedAccessException or TimeoutException)
        {
            missReason = failure.Message;
            return false;
        }
    }

    /// <summary>Atomically installs one verified entry and removes the oldest complete entries exceeding the disk budget.</summary>
    /// <param name="key">The same complete stage identity used for lookup.</param>
    /// <param name="artifacts">The entire successful candidate set, captured before postprocessing.</param>
    /// <param name="cancellationToken">Cancels verification or writing before installation.</param>
    /// <returns>False when the complete entry exceeds its budget; true after installation.</returns>
    public bool Store(
        ContentDigest key,
        ImmutableArray<CachedStageArtifact> artifacts,
        CancellationToken cancellationToken = default
    ) {
        if (artifacts.IsDefault) throw new ArgumentException("A cached stage requires a complete artifact array.", nameof(artifacts));
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        List<StoredArtifact> stored = [];
        long total = 0;
        foreach (CachedStageArtifact artifact in artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidatePath(artifact.descriptor.relativePath);
            if (!paths.Add(artifact.descriptor.relativePath) || string.IsNullOrWhiteSpace(artifact.descriptor.mediaType))
                throw new InvalidDataException("Cached artifact descriptors conflict.");
            byte[] content = artifact.content.ToArray();
            if (Convert.ToHexStringLower(SHA256.HashData(content)) != artifact.descriptor.digest.value)
                throw new InvalidDataException("A candidate artifact changed before cache capture.");
            total = checked(total + content.Length);
            if (total > m_maximumEntryBytes) return false;
            stored.Add(new(artifact.descriptor.relativePath, artifact.descriptor.mediaType, artifact.descriptor.digest.value, content));
        }
        StoredArtifact[] frozen = stored.ToArray();
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new Entry(C_REVISION, key.value, frozen, Seal(C_REVISION, key.value, frozen)));
        if (bytes.Length > m_maximumEntryBytes) return false;
        RejectLinks();
        Directory.CreateDirectory(m_root);
        using FileStream cacheLease = AcquireCacheLease(cancellationToken);
        string temporary = Path.Combine(m_root, ".write-" + Guid.NewGuid().ToString("N") + ".tmp");
        Exception? failure = null;
        try
        {
            using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            RejectLinks();
            string entry = EntryPath(key);
            RejectLink(entry);
            File.Move(temporary, entry, overwrite: true);
            Prune(cancellationToken);
            RejectLinks();
            return true;
        }
        catch (Exception error) { failure = error; throw; }
        finally { OwnedFileCleanup.Delete(temporary, failure); }
    }

    private void Prune(CancellationToken cancellationToken)
    {
        FileInfo[] entries = new DirectoryInfo(m_root).EnumerateFiles("*.stage.json", SearchOption.TopDirectoryOnly)
            .Where(file => file.Name.Length == 75 && IsDigest(file.Name[..64]))
            .OrderByDescending(file => file.LastWriteTimeUtc).ThenBy(file => file.Name, StringComparer.Ordinal).ToArray();
        long retained = 0;
        foreach (FileInfo entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectLink(entry.FullName);
            retained = checked(retained + entry.Length);
            if (retained > m_maximumBytes)
            {
                try { File.Delete(entry.FullName); }
                catch (FileNotFoundException) { }
            }
        }
    }

    private string EntryPath(ContentDigest key) => Path.Combine(m_root, new ContentDigest(key.value).value + ".stage.json");
    private FileStream AcquireCacheLease(CancellationToken cancellationToken)
    {
        // Readers update recency after validation. Serialize that handle with
        // installs/pruning as well as other writers, including other processes.
        // Retain one inode so releasing ownership cannot split the lock domain.
        string scope = Path.Combine(m_root, "entries");
        RejectLink(DirectoryPublicationLease.GetPath(scope));
        try { return DirectoryPublicationLease.Acquire(scope, TimeSpan.FromSeconds(30), cancellationToken); }
        catch (TimeoutException failure)
        {
            throw new IOException("Another generation cache operation retained entry-set ownership beyond the bounded wait.", failure);
        }
    }
    private static string Seal(
        int revision,
        string key,
        StoredArtifact[] artifacts
    )
        => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new ArtifactSetIdentity(revision, key,
            artifacts.Select(artifact => new ArtifactIdentity(artifact.path, artifact.mediaType, artifact.digest)).ToArray()))));
    private static bool IsDigest(string text)
    {
        try { _ = new ContentDigest(text); return true; }
        catch (ArgumentException) { return false; }
    }
    private static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || Path.IsPathRooted(path)
            || path.Split('/').Any(segment => segment is "" or "." or ".." || segment.Contains(':')))
            throw new InvalidDataException("A cached artifact path escapes its candidate scope.");
    }
    private void RejectLinks()
    {
        m_boundary.Verify();
    }
    private static void RejectLink(string path)
        => OwnedPathBoundary.RejectLink(path);
    private sealed record Entry(
        int revision,
        string key,
        StoredArtifact[] artifacts,
        string seal
    );
    private sealed record ArtifactSetIdentity(
        int revision,
        string key,
        ArtifactIdentity[] artifacts
    );
    private sealed record ArtifactIdentity(
        string path,
        string mediaType,
        string digest
    );
    private sealed record StoredArtifact(
        string path,
        string mediaType,
        string digest,
        byte[] content
    );
}

/// <summary>Pairs a verified logical artifact descriptor with independently captured candidate bytes.</summary>
public sealed class CachedStageArtifact
{
    private readonly byte[] m_content;
    /// <summary>Copies an artifact's complete content so later candidate edits cannot mutate the cache input.</summary>
    /// <param name="descriptor">The exact logical path, media type and SHA-256 digest.</param>
    /// <param name="content">The entire artifact content.</param>
    public CachedStageArtifact(
        ArtifactDescriptor descriptor,
        ReadOnlyMemory<byte> content
    ) {
        ArgumentNullException.ThrowIfNull(descriptor);
        this.descriptor = descriptor;
        m_content = content.ToArray();
    }
    /// <summary>Gets the exact candidate descriptor.</summary>
    public ArtifactDescriptor descriptor { get; }
    /// <summary>Gets an independent copy of the complete artifact bytes.</summary>
    public ReadOnlyMemory<byte> content => m_content.ToArray();
}
