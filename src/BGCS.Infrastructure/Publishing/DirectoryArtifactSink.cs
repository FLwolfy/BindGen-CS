using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BGCS.Contracts.Artifacts;
using BGCS.Infrastructure.IO;

namespace BGCS.Infrastructure.Publishing;

/// <summary>Writes backend artifacts inside one caller-owned staging directory.</summary>
public sealed class DirectoryArtifactSink : IArtifactSink
{
    private readonly string m_root;
    private readonly OwnedPathBoundary m_boundary;
    private readonly HashSet<string> m_paths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Captures a staging root without creating or publishing it.</summary>
    /// <param name="root">The caller-owned candidate directory.</param>
    /// <exception cref="ArgumentException">The root is empty.</exception>
    public DirectoryArtifactSink(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        m_boundary = new(root);
        m_root = m_boundary.physicalPath;
    }

    /// <inheritdoc/>
    public string WriteText(
        string relativePath,
        string content
    ) {
        m_boundary.Verify();
        ArgumentNullException.ThrowIfNull(content);
        return WriteBytes(relativePath, new UTF8Encoding(false).GetBytes(content));
    }

    /// <inheritdoc/>
    public string WriteBytes(
        string relativePath,
        ReadOnlyMemory<byte> content
    ) {
        m_boundary.Verify();
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        string normalized = relativePath.Replace('\\', '/');
        if (Path.IsPathRooted(normalized) || normalized.Split('/').AnyInvalidSegment())
            throw new ArgumentException("Artifact paths must identify a file inside the candidate root.", nameof(relativePath));
        string destination = Path.GetFullPath(normalized, m_root);
        string relative = Path.GetRelativePath(m_root, destination);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new ArgumentException("The artifact path escapes the candidate root.", nameof(relativePath));
        if (!m_paths.Add(normalized))
            throw new InvalidOperationException($"Two backend outputs claim artifact '{normalized}'.");
        string current = m_root;
        RejectLinkedAncestors(current);
        foreach (string segment in normalized.Split('/'))
        {
            current = Path.Combine(current, segment);
            RejectLink(current);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        m_boundary.Verify();
        OwnedPathBoundary.RejectPhysicalLinks(destination);
        using FileStream stream = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(content.Span);
        m_boundary.Verify();
        return destination;
    }

    private static void RejectLink(string path)
        => OwnedPathBoundary.RejectLink(path);

    private static void RejectLinkedAncestors(string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
            RejectLink(current);
    }
}

internal static class ArtifactPathSegments
{
    internal static bool AnyInvalidSegment(this string[] segments)
    {
        foreach (string segment in segments)
        {
            if (segment is "" or "." or ".." || segment.Contains(':'))
                return true;
        }
        return false;
    }
}
