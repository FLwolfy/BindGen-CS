using BGCS.Core.Hosting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BGCS.Core.Artifacts;
using BGCS.Infrastructure.IO;

namespace BGCS.Infrastructure.Verification;

/// <summary>Checks the exact artifact collection before it can be published or restored from cache.</summary>
public sealed class ArtifactDigestVerifier
{
    private readonly IHostPathService m_paths;
    /// <summary>Captures the explicitly selected host filesystem semantics.</summary>
    /// <param name="paths">The host path service.</param>
    public ArtifactDigestVerifier(IHostPathService paths) => m_paths = paths ?? throw new ArgumentNullException(nameof(paths));
    /// <summary>Validates portable identities, links, the complete file collection and each content digest.</summary>
    /// <param name="root">The owned artifact directory.</param>
    /// <param name="artifacts">The expected immutable artifact descriptors.</param>
    /// <param name="additionalFiles">Explicit nonartifact metadata paths, such as an ownership manifest.</param>
    /// <exception cref="IOException">A file is absent, altered, linked or unlisted.</exception>
    public void Verify(
        string root,
        IEnumerable<ArtifactDescriptor> artifacts,
        IEnumerable<string>? additionalFiles = null
    ) {
        ArgumentNullException.ThrowIfNull(artifacts);
        OwnedPathBoundary boundary = new(root, m_paths);
        string directory = boundary.physicalPath;
        Dictionary<string, ArtifactDescriptor> expected = new(m_paths.comparer);
        foreach (ArtifactDescriptor artifact in artifacts)
        {
            string path = ValidateRelativePath(artifact.relativePath);
            if (!expected.TryAdd(Path.GetFullPath(path, directory), artifact)) throw new IOException($"Duplicate artifact path '{path}'.");
        }
        HashSet<string> additional = new((additionalFiles ?? []).Select(ValidateRelativePath).Select(path => Path.GetFullPath(path, directory)), m_paths.comparer);
        if (additional.Overlaps(expected.Keys)) throw new IOException("An artifact path is also reserved as publication metadata.");
        HashSet<string> seen = new(m_paths.comparer);
        foreach (string file in EnumerateFiles(directory, m_paths))
        {
            string path = m_paths.GetRelativePath(directory, file).Replace('\\', '/');
            if (!seen.Add(file)) throw new IOException($"Artifact case collision at '{path}'.");
            if (additional.Contains(file)) continue;
            if (!expected.TryGetValue(file, out ArtifactDescriptor? artifact)) throw new IOException($"Unlisted artifact '{path}' cannot be published.");
            using FileStream stream = new(file, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.SequentialScan);
            string hash = Convert.ToHexStringLower(SHA256.HashData(stream));
            if (hash != artifact.digest.value) throw new IOException($"Artifact content digest mismatch: '{path}'.");
        }
        foreach (string required in expected.Keys.Concat(additional))
            if (!seen.Contains(required)) throw new IOException($"Required artifact '{required}' is missing.");
        boundary.Verify();
        m_paths.Verify();
    }

    /// <summary>Validates and normalizes a portable artifact-relative identity without accessing the filesystem.</summary>
    /// <param name="path">The relative file or directory identity.</param>
    /// <returns>The normalized forward-slash identity.</returns>
    /// <exception cref="IOException">The identity is absolute, traverses parents or contains unsupported filename characters.</exception>
    public static string ValidateRelativePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string normalized = path.Replace('\\', '/');
        if (Path.IsPathRooted(normalized) || normalized.Split('/').Any(segment => segment is "" or "." or ".." || segment.Contains(':') || segment.IndexOfAny(['\0', '<', '>', '"', '|', '?', '*']) >= 0 || segment.EndsWith(' ') || segment.EndsWith('.')))
            throw new IOException($"Artifact path '{path}' is not portable and candidate-relative.");
        return normalized;
    }

    internal static void RejectLinkedAncestors(string path)
        => OwnedPathBoundary.RejectPhysicalLinks(path);

    /// <summary>Enumerates a link-free owned artifact tree while retaining its captured physical boundary.</summary>
    /// <param name="directory">The owned artifact root.</param>
    /// <param name="paths">The explicitly supplied host path identity service.</param>
    /// <returns>The absolute paths of regular artifact files.</returns>
    public static IEnumerable<string> EnumerateFiles(
        string directory,
        IHostPathService paths
    ) {
        OwnedPathBoundary boundary = new(directory, paths);
        foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
        {
            FileAttributes attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException($"Linked artifact '{entry}' is forbidden.");
            if ((attributes & FileAttributes.Directory) != 0)
            {
                foreach (string nested in EnumerateFiles(entry, paths)) yield return nested;
            }
            else yield return entry;
        }
        boundary.Verify();
    }
}
