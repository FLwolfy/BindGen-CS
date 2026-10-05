using System;
using System.IO;
using System.Threading;

namespace BGCS.Core.IO;

internal sealed class DirectoryPublicationOwnership : IDisposable
{
    private readonly FileStream m_first;
    private readonly FileStream m_second;

    internal DirectoryPublicationOwnership(
        string first,
        string second
    ) {
        string firstPath = DirectoryPublicationLease.GetPath(first);
        string secondPath = DirectoryPublicationLease.GetPath(second);
        StringComparer comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        if (comparer.Equals(firstPath, secondPath))
            throw new ArgumentException("Publication requires two different output directories.", nameof(second));
        if (comparer.Compare(firstPath, secondPath) > 0)
            (first, second) = (second, first);
        m_first = DirectoryPublicationLease.Acquire(first, TimeSpan.FromMinutes(5), CancellationToken.None);
        try
        {
            m_second = DirectoryPublicationLease.Acquire(second, TimeSpan.FromMinutes(5), CancellationToken.None);
        }
        catch
        {
            m_first.Dispose();
            throw;
        }
    }

    internal FileStream GetLease(string directory)
    {
        string path = DirectoryPublicationLease.GetPath(directory);
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(m_first.Name, path, comparison) ? m_first
            : string.Equals(m_second.Name, path, comparison) ? m_second
            : throw new InvalidOperationException("This ownership does not cover the requested directory.");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            m_second.Dispose();
        }
        finally
        {
            m_first.Dispose();
        }
    }
}
