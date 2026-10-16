using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;

namespace BGCS.Infrastructure.IO;

/// <summary>Detects added, removed and redirected header candidates independently of filename extensions.</summary>
public static class DirectoryMembershipDigest
{
    /// <summary>Captures complete recursive membership without hashing file contents.</summary>
    /// <param name="directory">An effective include search root, including a currently missing root.</param>
    /// <param name="excludedDirectories">Explicit private output roots; actual read files remain separately hashed.</param>
    /// <param name="cancellationToken">Cancels membership traversal.</param>
    /// <returns>An immutable search-root dependency.</returns>
    public static DirectoryInputDependency Capture(
        string directory,
        IEnumerable<string>? excludedDirectories = null,
        CancellationToken cancellationToken = default
    ) {
        string logical = Path.GetFullPath(directory);
        string physical = PhysicalPathResolver.Resolve(logical);
        bool exists = Directory.Exists(logical);
        string[] exclusions = (excludedDirectories ?? []).Select(path => PhysicalPathResolver.Resolve(Path.GetFullPath(path))).ToArray();
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(logical);
        Append(physical);
        Append(exists ? "exists" : "missing");
        HashSet<string> visited = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        Stack<(string Path, string Relative)> pending = new();
        if (exists && !Excluded(physical))
            pending.Push((physical, string.Empty));
        while (pending.TryPop(out var item))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string resolved = PhysicalPathResolver.Resolve(item.Path);
            if (!visited.Add(resolved))
                continue;
            foreach (FileSystemInfo entry in new DirectoryInfo(resolved).EnumerateFileSystemInfos().OrderBy(entry => entry.Name, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileAttributes attributes = entry.Attributes;
                string? linkTarget = (attributes & FileAttributes.ReparsePoint) != 0 ? entry.LinkTarget : null;
                if (exclusions.Length != 0 && Excluded(linkTarget is null ? entry.FullName : PhysicalPathResolver.Resolve(entry.FullName))) continue;
                string relative = item.Relative + entry.Name;
                bool isDirectory = (attributes & FileAttributes.Directory) != 0;
                Append(relative);
                Append(isDirectory ? "directory" : "file");
                Append(linkTarget ?? string.Empty);
                if (linkTarget is not null)
                    Append(PhysicalPathResolver.Resolve(entry.FullName));
                if (isDirectory)
                    pending.Push((entry.FullName, relative + "/"));
            }
        }
        return new(logical, physical, exists, new ContentDigest(Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()));

        bool Excluded(string path) => exclusions.Any(root => string.Equals(path, root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            || path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));

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
