using System;
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
    {
        string absolute = Path.GetFullPath(path);
        string root = Path.GetPathRoot(absolute)!;
        string current = root;
        foreach (string component in absolute[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            FileAttributes attributes;
            try { attributes = File.GetAttributes(current); }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                FileSystemInfo entry = (attributes & FileAttributes.Directory) != 0 ? new DirectoryInfo(current) : new FileInfo(current);
                if (entry.LinkTarget is not null)
                    current = (entry.ResolveLinkTarget(returnFinalTarget: true)
                        ?? throw new IOException($"Input link could not be resolved: '{current}'.")).FullName;
            }
        }
        return Path.GetFullPath(current);
    }
}
