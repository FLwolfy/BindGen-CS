using System;
using System.Collections.Generic;
using System.IO;

namespace BGCS.Infrastructure.IO;

/// <summary>Resolves symbolic links at every path component instead of only the final file.</summary>
public static class PhysicalPathResolver
{
    /// <summary>Resolves existing links while preserving nonexistent trailing components.</summary>
    /// <param name="path">The logical path.</param>
    /// <returns>The absolute physical identity.</returns>
    /// <exception cref="IOException">A link is cyclic or cannot be resolved.</exception>
    public static string Resolve(string path)
        => ResolveCore(Path.GetFullPath(path), new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal), 0);

    private static string ResolveCore(
        string absolute,
        HashSet<string> activeLinks,
        int depth
    ) {
        if (depth > 128) throw new IOException("The path exceeds the symbolic-link resolution limit.");
        string root = Path.GetPathRoot(absolute)!;
        string current = root;
        foreach (string component in absolute[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            FileAttributes? attributes = null;
            try { attributes = File.GetAttributes(current); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            if (attributes is null || (attributes.Value & FileAttributes.ReparsePoint) != 0)
            {
                FileSystemInfo entry = attributes is not null && (attributes.Value & FileAttributes.Directory) != 0 ? new DirectoryInfo(current) : new FileInfo(current);
                if (entry.LinkTarget is string target)
                {
                    string link = current;
                    if (!activeLinks.Add(link)) throw new IOException($"Input link is cyclic: '{link}'.");
                    try
                    {
                        // Resolve the target's ancestors too. ResolveLinkTarget(true) only
                        // follows the final link and can return another logical parent alias.
                        current = ResolveCore(Path.GetFullPath(target, Path.GetDirectoryName(link)!), activeLinks, depth + 1);
                    }
                    finally { activeLinks.Remove(link); }
                }
            }
        }
        return Path.GetFullPath(current);
    }
}
