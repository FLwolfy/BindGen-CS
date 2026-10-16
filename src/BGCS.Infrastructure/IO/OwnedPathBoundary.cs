using System;
using System.IO;

namespace BGCS.Infrastructure.IO;

/// <summary>Captures a caller-selected parent alias without permitting linked owned leaves or later redirection.</summary>
internal sealed class OwnedPathBoundary
{
    private readonly string m_logical;
    internal string physicalPath { get; }

    internal OwnedPathBoundary(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        m_logical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string parent = Path.GetDirectoryName(m_logical) ?? throw new IOException("An owned path must have a parent directory.");
        RejectLink(m_logical);
        physicalPath = Path.Combine(PhysicalPathResolver.Resolve(parent), Path.GetFileName(m_logical));
        Verify();
    }

    internal void Verify()
    {
        RejectLink(m_logical);
        if (!Equal(PhysicalPathResolver.Resolve(m_logical), physicalPath))
            throw new IOException($"The owned path changed physical identity: '{m_logical}'.");
        RejectPhysicalLinks(physicalPath);
    }

    internal static void RejectPhysicalLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            RejectLink(current);
    }

    internal static void RejectLink(string path)
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

    private static bool Equal(
        string first,
        string second
    )
        => string.Equals(Path.TrimEndingDirectorySeparator(first), Path.TrimEndingDirectorySeparator(second), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
