using BGCS.Core.Hosting;
using System;
using System.IO;
using System.Threading;

namespace BGCS.Infrastructure.IO;

internal sealed class DirectoryPublicationOwnership : IDisposable
{
    private readonly IHostPathService m_paths;
    private readonly FileStream m_first;
    private readonly FileStream m_second;

    internal DirectoryPublicationOwnership(
        string first,
        string second,
        IHostPathService paths
    ) {
        m_paths = paths;
        string firstPath = DirectoryPublicationLease.GetPath(first, paths);
        string secondPath = DirectoryPublicationLease.GetPath(second, paths);

        if (paths.Equals(firstPath, secondPath))
            throw new ArgumentException("Publication requires two different output directories.", nameof(second));
        if (StringComparer.Ordinal.Compare(paths.GetIdentityKey(firstPath), paths.GetIdentityKey(secondPath)) > 0)
            (first, second) = (second, first);
        m_first = DirectoryPublicationLease.Acquire(first, paths, TimeSpan.FromMinutes(5), CancellationToken.None);
        try
        {
            m_second = DirectoryPublicationLease.Acquire(second, paths, TimeSpan.FromMinutes(5), CancellationToken.None);
        }
        catch
        {
            m_first.Dispose();
            throw;
        }
    }

    internal FileStream GetLease(string directory)
    {
        string path = DirectoryPublicationLease.GetPath(directory, m_paths);

        return m_paths.Equals(m_first.Name, path) ? m_first
            : m_paths.Equals(m_second.Name, path) ? m_second
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
