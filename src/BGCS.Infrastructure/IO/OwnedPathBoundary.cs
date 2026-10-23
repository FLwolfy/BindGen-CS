using System;
using System.IO;
using BGCS.Core.Hosting;

namespace BGCS.Infrastructure.IO;

/// <summary>Captures a caller-selected parent alias without permitting linked owned leaves or later redirection.</summary>
public sealed class OwnedPathBoundary
{
    private readonly string m_logical;
    private readonly IHostPathService m_paths;
    /// <summary>Gets the frozen physical owner location.</summary>
    public string physicalPath { get; }

    /// <summary>Captures the parent alias and rejects linked owned leaves.</summary>
    /// <param name="path">The proposed owned path.</param>
    /// <param name="paths">The explicitly selected host path service.</param>
    public OwnedPathBoundary(
        string path,
        IHostPathService paths
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        m_paths = paths ?? throw new ArgumentNullException(nameof(paths));
        m_logical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string parent = Path.GetDirectoryName(m_logical) ?? throw new IOException("An owned path must have a parent directory.");
        RejectLink(m_logical);
        physicalPath = Path.Combine(PhysicalPathResolver.Resolve(parent, paths), Path.GetFileName(m_logical));
        Verify();
    }

    /// <summary>Rechecks physical identity and every owned physical ancestor.</summary>
    public void Verify()
    {
        RejectLink(m_logical);
        if (!m_paths.Equals(PhysicalPathResolver.Resolve(m_logical, m_paths), physicalPath))
            throw new IOException($"The owned path changed physical identity: '{m_logical}'.");
        RejectPhysicalLinks(physicalPath);
    }

    /// <summary>Rejects existing or dangling links at every physical path component.</summary>
    /// <param name="path">The physical owner path.</param>
    public static void RejectPhysicalLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            RejectLink(current);
    }

    /// <summary>Rejects an existing or dangling link at one owned path.</summary>
    /// <param name="path">The owned path.</param>
    public static void RejectLink(string path)
    {
        FileAttributes attributes;
        try { attributes = File.GetAttributes(path); }
        catch (FileNotFoundException) { RejectDanglingLink(path); return; }
        catch (DirectoryNotFoundException) { RejectDanglingLink(path); return; }
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"An owned path cannot follow a symbolic link or reparse point: '{path}'.");
    }

    private static void RejectDanglingLink(string path)
    {
        if (new FileInfo(path).LinkTarget is not null)
            throw new IOException($"An owned path cannot follow a dangling symbolic link: '{path}'.");
    }

}
