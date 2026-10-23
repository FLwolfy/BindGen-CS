using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Hosting;

namespace BGCS.Infrastructure.IO;

/// <summary>Detects added, removed and redirected header candidates independently of filename extensions.</summary>
public static class DirectoryMembershipDigest
{
    /// <summary>Captures complete recursive membership without hashing file contents.</summary>
    /// <param name="directory">An effective include search root, including a currently missing root.</param>
    /// <param name="paths">The explicitly selected host path identity service.</param>
    /// <param name="excludedDirectories">Explicit private output roots; actual read files remain separately hashed.</param>
    /// <param name="cancellationToken">Cancels membership traversal.</param>
    /// <returns>An immutable search-root dependency.</returns>
    public static DirectoryInputDependency Capture(
        string directory,
        IHostPathService paths,
        IEnumerable<string>? excludedDirectories = null,
        CancellationToken cancellationToken = default
    ) {
        DirectoryInputDependency captured = CaptureWithinBoundary(directory, paths, excludedDirectories, cancellationToken);
        paths.Verify();
        if (Directory.Exists(captured.resolvedPath) != captured.exists)
            throw new IOException("The directory root's existence changed during membership capture: " + captured.path);
        cancellationToken.ThrowIfCancellationRequested();
        return captured;
    }

    internal static DirectoryInputDependency CaptureWithinBoundary(
        string directory,
        IHostPathService paths,
        IEnumerable<string>? excludedDirectories,
        CancellationToken cancellationToken
    ) {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(paths);
        PhysicalPathResolver.ResolutionScope resolution = new(paths);
        string logical = Path.GetFullPath(directory);
        string physical = resolution.Resolve(logical, cancellationToken);
        bool exists = Directory.Exists(physical);
        string[] exclusions = (excludedDirectories ?? []).Select(path => resolution.Resolve(Path.GetFullPath(path), cancellationToken)).ToArray();
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(logical);
        Append(physical);
        Append(exists ? "exists" : "missing");
        HashSet<string> visited = new(paths.comparer);
        Stack<(string Path, string Relative)> pending = new();
        if (exists && !Excluded(physical))
            pending.Push((physical, string.Empty));
        while (pending.TryPop(out var item))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string resolved = resolution.Resolve(item.Path, cancellationToken);
            if (!visited.Add(resolved))
                continue;
            Append(paths.GetSemantics(resolved).caseSensitive ? "case-sensitive" : "case-insensitive");
            foreach (FileSystemInfo entry in new DirectoryInfo(resolved).EnumerateFileSystemInfos().OrderBy(entry => entry.Name, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Enumeration already supplies the entry kind. Attributes also
                // computes unrelated Unix user/group write permissions.
                string? linkTarget = entry.LinkTarget;
                string? resolvedTarget = linkTarget is null ? null : resolution.Resolve(entry.FullName, cancellationToken);
                if (exclusions.Length != 0 && Excluded(resolvedTarget ?? entry.FullName)) continue;
                string relative = item.Relative + entry.Name;
                bool isDirectory = entry is DirectoryInfo;
                Append(relative);
                Append(isDirectory ? "directory" : "file");
                Append(linkTarget ?? string.Empty);
                if (resolvedTarget is not null)
                    Append(resolvedTarget);
                if (isDirectory && (resolvedTarget is null || Directory.Exists(resolvedTarget)))
                    pending.Push((entry.FullName, relative + "/"));
            }
        }
        resolution.Verify(cancellationToken);
        if (Directory.Exists(physical) != exists) throw new IOException("The directory root's existence changed during membership capture: " + logical);
        cancellationToken.ThrowIfCancellationRequested();
        return new(logical, physical, exists, new ContentDigest(Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()));

        bool Excluded(string path) => exclusions.Any(root => paths.IsWithin(path, root));

        void Append(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value.Replace('\\', '/'));
            Span<byte> length = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }
    }
}
