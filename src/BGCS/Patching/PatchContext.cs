using System;
using System.Collections.Generic;
using System.IO;

namespace BGCS.Patching;

/// <summary>
/// Reads and rewrites relative files inside one candidate output directory owned by the caller.
/// </summary>
public sealed class PatchContext
{
    private readonly string m_stage;
    private readonly List<string> m_writtenFiles = [];
    private readonly HashSet<string> m_writtenFileSet = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    /// <summary>
    /// Captures a candidate directory without publishing it or taking ownership of its lifetime.
    /// </summary>
    /// <param name="stage">
    /// The candidate root. File operations create missing subdirectories as required.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The candidate directory path is empty or invalid.
    /// </exception>
    /// <exception cref="IOException">
    /// The candidate root is a symbolic link or reparse point.
    /// </exception>
    public PatchContext(string stage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        m_stage = Path.GetFullPath(stage);
        RejectLink(m_stage);
    }

    /// <summary>
    /// Resolves a relative file name under this candidate and rejects traversal or linked path components.
    /// </summary>
    /// <param name="path">
    /// A relative file name within the candidate directory.
    /// </param>
    /// <returns>
    /// The absolute candidate path; the file need not exist yet.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// The name is empty, rooted, names the directory itself, or escapes the candidate.
    /// </exception>
    /// <exception cref="IOException">
    /// An existing path component inside the candidate is a symbolic link or reparse point.
    /// </exception>
    public string GetFullPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Path.IsPathRooted(path))
            throw new ArgumentException("Patch paths must be relative to their owned directory.", nameof(path));
        string fullPath = Path.GetFullPath(path, m_stage);
        string relative = Path.GetRelativePath(m_stage, fullPath);
        if (relative == "." || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
            throw new ArgumentException("Patch paths must identify a file within their owned directory.", nameof(path));
        RejectLink(m_stage);
        string current = m_stage;
        foreach (string segment in relative.Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            RejectLink(current);
        }
        return fullPath;
    }

    /// <summary>
    /// Reads a candidate source using the platform's UTF-8 text-file conventions.
    /// </summary>
    /// <param name="path">
    /// A relative candidate file name.
    /// </param>
    /// <returns>
    /// The complete text, including an empty string for an empty file.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// The path is not a relative file within the candidate.
    /// </exception>
    /// <exception cref="IOException">
    /// The file cannot be read or a candidate path component is linked.
    /// </exception>
    public string ReadFile(string path) => File.ReadAllText(GetFullPath(path));

    /// <summary>
    /// Replaces candidate text and records the normalized file name for subsequent patch stages.
    /// </summary>
    /// <param name="path">
    /// A relative candidate file name; missing parent directories are created.
    /// </param>
    /// <param name="content">
    /// The complete UTF-8 replacement text; null creates an empty file.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The path is not a relative file within the candidate.
    /// </exception>
    /// <exception cref="IOException">
    /// The file cannot be written or a candidate path component is linked.
    /// </exception>
    public void WriteFile(
        string path,
        string? content
    ) {
        string fullPath = GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        string relativePath = Path.GetRelativePath(m_stage, fullPath);
        if (m_writtenFileSet.Add(relativePath))
            m_writtenFiles.Add(relativePath);
    }

    internal IReadOnlyList<string> writtenFiles => m_writtenFiles;

    internal void CopyFromInput(string root)
    {
        string sourceRoot = Path.GetFullPath(root);
        RejectLink(sourceRoot);
        Stack<string> directories = new();
        directories.Push(sourceRoot);
        while (directories.TryPop(out string? directory))
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                RejectLink(entry);
                if (Directory.Exists(entry))
                {
                    directories.Push(entry);
                    continue;
                }
                string destination = GetFullPath(Path.GetRelativePath(sourceRoot, entry));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(entry, destination, overwrite: true);
            }
        }
    }

    private static void RejectLink(string path)
    {
        if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Patch output cannot contain symbolic links or reparse points: {path}");
    }
}
