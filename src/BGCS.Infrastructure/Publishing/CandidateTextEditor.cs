using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;

namespace BGCS.Infrastructure.Publishing;

/// <summary>Replaces exact candidate text with digest verification and a complete updated descriptor snapshot.</summary>
public sealed class CandidateTextEditor : IArtifactEditor, IDisposable
{
    private readonly string m_root;
    private readonly Dictionary<string, ArtifactDescriptor> m_artifacts;
    private readonly SemaphoreSlim m_gate = new(1, 1);
    private bool m_closed;
    private readonly string? m_additionPrefix;

    /// <summary>Captures the finite source set that this processing stage is permitted to edit.</summary>
    /// <param name="root">The owned candidate root.</param>
    /// <param name="artifacts">The exact candidate text descriptors.</param>
    /// <param name="additionPrefix">An optional portable directory prefix for new candidates; null permits replacements only.</param>
    public CandidateTextEditor(
        string root,
        IEnumerable<ArtifactDescriptor> artifacts,
        string? additionPrefix = null
    ) {
        m_root = Path.GetFullPath(root);
        m_artifacts = artifacts.ToDictionary(artifact => artifact.relativePath, StringComparer.Ordinal);
        _ = new DirectoryArtifactReader(m_root, m_artifacts.Values);
        if (additionPrefix is not null)
        {
            if (!additionPrefix.EndsWith('/') || additionPrefix.Length == 1) throw new ArgumentException("An additional artifact prefix must name a portable directory.");
            _ = new DirectoryArtifactReader(m_root, [new(additionPrefix + "__path_validation__", "text/plain", new ContentDigest(new string('0', 64)))]);
        }
        m_additionPrefix = additionPrefix;
    }

    /// <summary>Gets a complete immutable snapshot of all edited candidate identities.</summary>
    public ImmutableArray<ArtifactDescriptor> artifacts
    {
        get
        {
            m_gate.Wait();
            try { ObjectDisposedException.ThrowIf(m_closed, this); return m_artifacts.Values.OrderBy(artifact => artifact.relativePath, StringComparer.Ordinal).ToImmutableArray(); }
            finally { m_gate.Release(); }
        }
    }

    /// <inheritdoc/>
    public async ValueTask<ReadOnlyMemory<byte>> ReadBytesAsync(
        string relativePath,
        CancellationToken cancellationToken
    ) {
        await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(m_closed, this);
            return await new DirectoryArtifactReader(m_root, m_artifacts.Values).ReadBytesAsync(relativePath, cancellationToken).ConfigureAwait(false);
        }
        finally { m_gate.Release(); }
    }

    /// <inheritdoc/>
    public async ValueTask<string> ReadTextAsync(
        string relativePath,
        CancellationToken cancellationToken
    )
        => new UTF8Encoding(false, true).GetString((await ReadBytesAsync(relativePath, cancellationToken).ConfigureAwait(false)).Span);

    /// <inheritdoc/>
    public async ValueTask<ArtifactDescriptor> ReplaceTextAsync(
        string relativePath,
        string content,
        CancellationToken cancellationToken
    ) {
        ArgumentNullException.ThrowIfNull(content);
        await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporary = null;
        try
        {
            ObjectDisposedException.ThrowIf(m_closed, this);
            if (!m_artifacts.TryGetValue(relativePath, out ArtifactDescriptor? previous)) throw new ArgumentException("Only declared candidate artifacts can be replaced.");
            if (!previous.mediaType.StartsWith("text/", StringComparison.Ordinal) && previous.mediaType != "application/json") throw new ArgumentException("A text processor cannot replace a binary candidate.");
            _ = await new DirectoryArtifactReader(m_root, m_artifacts.Values).ReadBytesAsync(relativePath, cancellationToken).ConfigureAwait(false);
            byte[] bytes = new UTF8Encoding(false, true).GetBytes(content);
            if (bytes.Length > 256 * 1024 * 1024) throw new IOException("A replacement text artifact exceeds the complete candidate read limit.");
            string destination = Path.GetFullPath(relativePath, m_root);
            temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".bgcs-edit-" + Guid.NewGuid().ToString("N"));
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
            temporary = null;
            ArtifactDescriptor replacement = previous with { digest = new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(bytes))) };
            m_artifacts[relativePath] = replacement;
            return replacement;
        }
        finally
        {
            if (temporary is not null && File.Exists(temporary)) File.Delete(temporary);
            m_gate.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask<ArtifactDescriptor> AddTextAsync(
        string relativePath,
        string mediaType,
        string content,
        CancellationToken cancellationToken
    ) {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? installed = null;
        try
        {
            ObjectDisposedException.ThrowIf(m_closed, this);
            if (m_additionPrefix is null || !relativePath.StartsWith(m_additionPrefix, StringComparison.Ordinal)) throw new ArgumentException("An additional artifact must remain inside the stage's owned output prefix.");
            if (!mediaType.StartsWith("text/", StringComparison.Ordinal) && mediaType != "application/json") throw new ArgumentException("A text processing stage cannot add binary candidates.");
            if (m_artifacts.Keys.Any(path => string.Equals(path, relativePath, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("The additional candidate path conflicts with an existing artifact.");
            byte[] bytes = new UTF8Encoding(false, true).GetBytes(content);
            if (bytes.Length > 256 * 1024 * 1024) throw new IOException("The additional text artifact exceeds the complete-read limit.");
            ArtifactDescriptor artifact = new(relativePath, mediaType, new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(bytes))));
            _ = new DirectoryArtifactReader(m_root, [artifact]);
            cancellationToken.ThrowIfCancellationRequested();
            installed = new DirectoryArtifactSink(m_root).WriteBytes(relativePath, bytes);
            m_artifacts.Add(relativePath, artifact);
            installed = null;
            return artifact;
        }
        finally
        {
            if (installed is not null && File.Exists(installed)) File.Delete(installed);
            m_gate.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask DeleteTextAsync(
        string relativePath,
        CancellationToken cancellationToken
    ) {
        await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(m_closed, this);
            if (!m_artifacts.TryGetValue(relativePath, out ArtifactDescriptor? previous)) throw new ArgumentException("Only a declared text candidate can be removed.");
            if (!previous.mediaType.StartsWith("text/", StringComparison.Ordinal) && previous.mediaType != "application/json") throw new ArgumentException("A text processing stage cannot remove binary candidates.");
            _ = await new DirectoryArtifactReader(m_root, m_artifacts.Values).ReadBytesAsync(relativePath, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(Path.GetFullPath(relativePath, m_root));
            m_artifacts.Remove(relativePath);
        }
        finally { m_gate.Release(); }
    }

    /// <summary>Closes the stage surface so retained processors cannot edit later pipeline phases.</summary>
    public void Dispose()
    {
        m_gate.Wait();
        try { m_closed = true; }
        finally { m_gate.Release(); }
    }
}
