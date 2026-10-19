using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace BGCS;

internal sealed record NativeGeneratedDirectoryMapping(
    string virtualId,
    string sourceDirectory,
    string artifactDirectory,
    string? logicalSourcePrefix = null
);
internal sealed record NativeGeneratedInputMap(
    string storeRoot,
    ImmutableArray<NativeGeneratedDirectoryMapping> directories
);

internal static class NativeGeneratedInputRelocation
{
    private static readonly StringComparer s_paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly StringComparison s_pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    internal static string? MapPath(
        string path,
        ImmutableArray<NativeGeneratedInputMap> maps
    ) {
        if (maps.IsDefaultOrEmpty) return null;
        foreach (NativeGeneratedDirectoryMapping directory in maps.SelectMany(map => map.directories))
        {
            if (!Within(path, directory.sourceDirectory)) continue;
            string relative = Path.GetRelativePath(directory.sourceDirectory, path).Replace('\\', '/');
            return "artifact:/" + directory.artifactDirectory + (relative == "." ? string.Empty : "/" + relative);
        }
        return null;
    }

    internal static ImmutableArray<string> EnumerateFiles(string directory)
    {
        ImmutableArray<string>.Builder files = ImmutableArray.CreateBuilder<string>();
        Stack<string> pending = new(); pending.Push(directory);
        while (pending.TryPop(out string? current))
        {
            DirectoryInfo root = new(current);
            if (root.LinkTarget is not null || (root.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Generated input directories cannot contain symbolic links or reparse points.");
            FileSystemInfo[] entries = root.EnumerateFileSystemInfos().OrderBy(entry => entry.Name, StringComparer.Ordinal).ToArray();
            if (entries.Length == 0 && current != directory) throw new IOException("Generated input directories cannot contain undeclared empty directories.");
            foreach (FileSystemInfo entry in entries)
            {
                if (entry.LinkTarget is not null || (entry.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Generated inputs cannot contain symbolic links or reparse points.");
                if ((entry.Attributes & FileAttributes.Directory) != 0) pending.Push(entry.FullName); else files.Add(entry.FullName);
            }
        }
        return files.ToImmutable();
    }
    private static bool Within(
        string path,
        string root
    ) => s_paths.Equals(Path.GetFullPath(path), Path.GetFullPath(root))
        || Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, s_pathComparison);
}
