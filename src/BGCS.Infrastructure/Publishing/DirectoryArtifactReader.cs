using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Infrastructure.IO;

namespace BGCS.Infrastructure.Publishing;

/// <summary>Reads exact owned candidate paths and rejects content or filesystem identity changes.</summary>
public sealed class DirectoryArtifactReader : IArtifactReader
{
    private readonly FrozenDictionary<string, ArtifactDescriptor> m_artifacts;
    private readonly string m_root;
    private readonly OwnedPathBoundary m_boundary;
    private readonly int m_limit;

    /// <summary>Captures a finite declared candidate set independently of a mutable manifest collection.</summary>
    /// <param name="root">The caller-owned candidate directory.</param>
    /// <param name="artifacts">The exact declared artifact set.</param>
    /// <param name="maximumArtifactBytes">The positive complete-read limit, defaulting to 256 MiB.</param>
    public DirectoryArtifactReader(
        string root,
        IEnumerable<ArtifactDescriptor> artifacts,
        int maximumArtifactBytes = 256 * 1024 * 1024
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(artifacts);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumArtifactBytes);
        m_boundary = new(root);
        m_root = m_boundary.physicalPath;
        m_limit = maximumArtifactBytes;
        m_artifacts = artifacts.ToFrozenDictionary(artifact => artifact.relativePath, StringComparer.Ordinal);
        foreach (string path in m_artifacts.Keys) _ = Resolve(path);
    }

    /// <inheritdoc/>
    public async ValueTask<ReadOnlyMemory<byte>> ReadBytesAsync(
        string relativePath,
        CancellationToken cancellationToken
    ) {
        cancellationToken.ThrowIfCancellationRequested();
        if (!m_artifacts.TryGetValue(relativePath, out ArtifactDescriptor? expected)) throw new ArgumentException("The requested candidate artifact is not declared.", nameof(relativePath));
        string path = Resolve(relativePath);
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > m_limit) throw new IOException("The candidate artifact exceeds the configured complete-read limit.");
        byte[] bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        if (stream.Length != bytes.Length || new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(bytes))) != expected.digest)
            throw new IOException("The candidate artifact changed after its content identity was declared.");
        cancellationToken.ThrowIfCancellationRequested();
        _ = Resolve(relativePath);
        return bytes;
    }

    /// <inheritdoc/>
    public async ValueTask<string> ReadTextAsync(
        string relativePath,
        CancellationToken cancellationToken
    )
        => new UTF8Encoding(false, true).GetString((await ReadBytesAsync(relativePath, cancellationToken).ConfigureAwait(false)).Span);

    private string Resolve(string relativePath)
    {
        m_boundary.Verify();
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath) || relativePath.Contains('\\') || relativePath.Split('/').AnyInvalidSegment())
            throw new ArgumentException("A candidate read requires an exact portable relative path.", nameof(relativePath));
        string path = Path.GetFullPath(relativePath, m_root);
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(m_root) + Path.DirectorySeparatorChar, comparison)) throw new ArgumentException("The candidate read escapes its root.");
        OwnedPathBoundary.RejectPhysicalLinks(path);
        if (!string.Equals(PhysicalPathResolver.Resolve(path), path, comparison)) throw new IOException("A candidate read changed physical identity.");
        return path;
    }
}
