using BGCS.Core.Hosting;
using System;
using System.IO;
using System.Threading;

namespace BGCS.Infrastructure.IO;

/// <summary>
/// Stages a complete output directory and replaces the destination only after an explicit commit.
/// </summary>
public sealed class OutputDirectoryTransaction : IDisposable
{
    private readonly IHostPathService m_paths;
    private readonly string m_destinationPath;
    private readonly OwnedPathBoundary m_boundary;
    private readonly FileStream m_publicationLease;
    private readonly bool m_ownsLease;
    private bool m_committed;
    private bool m_disposed;
    /// <summary>
    /// Creates a staging directory on the same volume as the destination.
    /// </summary>
    /// <param name="destinationPath">
    /// The final output directory replaced by <see cref="Commit"/>.
    /// </param>
    /// <param name="paths">The explicitly selected host path service.</param>
    /// <param name="lockTimeout">
    /// The maximum wait for another publisher, or null for five minutes. Infinite waiting is explicit.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels waiting for publication ownership before a staging directory is created.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The output path has no parent directory.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The lock timeout is negative and is not infinite.
    /// </exception>
    /// <exception cref="TimeoutException">
    /// Another publisher retains ownership beyond the requested timeout.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// The caller cancels before publication ownership is acquired.
    /// </exception>
    public OutputDirectoryTransaction(
        string destinationPath,
        IHostPathService paths,
        TimeSpan? lockTimeout = null,
        CancellationToken cancellationToken = default
    ) : this(new OwnedPathBoundary(destinationPath, paths), paths, null, ownsLease: true, lockTimeout ?? TimeSpan.FromMinutes(5), cancellationToken)
    {
    }

    /// <summary>
    /// Gets the directory into which all candidate output must be written.
    /// </summary>
    public string stagingPath { get; }

    internal OutputDirectoryTransaction(
        string destinationPath,
        IHostPathService paths,
        FileStream publicationLease
    ) : this(new OwnedPathBoundary(destinationPath, paths), paths, publicationLease, ownsLease: false, TimeSpan.Zero, CancellationToken.None)
    {
    }

    private OutputDirectoryTransaction(
        OwnedPathBoundary boundary,
        IHostPathService paths,
        FileStream? publicationLease,
        bool ownsLease,
        TimeSpan timeout,
        CancellationToken cancellationToken
    ) {
        m_paths = paths;
        m_boundary = boundary;
        m_destinationPath = boundary.physicalPath;
        string parent = Path.GetDirectoryName(m_destinationPath)
            ?? throw new ArgumentException("The output path must have a parent directory.", nameof(boundary));
        m_publicationLease = publicationLease ?? DirectoryPublicationLease.Acquire(m_destinationPath, paths, timeout, cancellationToken);
        m_ownsLease = ownsLease;
        try
        {
            m_boundary.Verify();
            // A destination fingerprint must not be repeated at every nested generation stage.
            stagingPath = Path.Combine(parent, $".bgcs-staging-{Guid.NewGuid():N}");
            Directory.CreateDirectory(stagingPath);
            m_boundary.Verify();
        }
        catch
        {
            if (m_ownsLease)
                m_publicationLease.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Promotes the complete candidate while retaining rollback ownership of the previous tree.
    /// </summary>
    /// <remarks>
    /// Readers must participate in publication ownership during the two directory moves.
    /// Host-classified temporary access and sharing failures are retried for at most two seconds per move.
    /// A persistent failure is reported and the previous tree is restored when installation fails.
    /// Backup cleanup runs after commit and cannot roll back an installed candidate.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">
    /// Publication ownership has already been released.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// This transaction has already committed its candidate.
    /// </exception>
    /// <exception cref="IOException">
    /// Installation fails, or the installed candidate's backup cannot be removed.
    /// A cleanup failure identifies both the committed destination and remaining backup.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">
    /// The operating system continues denying a directory move after the bounded retry window.
    /// </exception>
    /// <exception cref="AggregateException">
    /// Candidate installation and restoration of the previous tree both fail.
    /// </exception>
    public void Commit()
    {
        string? backupPath = InstallCandidate();
        RemoveBackup(backupPath);
    }

    internal string? InstallCandidate(
        string? preparedBackupPath = null,
        bool requireMissingDestination = false
    ) {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        m_boundary.Verify();
        OwnedPathBoundary.RejectPhysicalLinks(stagingPath);
        if (m_committed)
        {
            throw new InvalidOperationException("The output directory transaction has already been committed.");
        }

        if (File.Exists(m_destinationPath))
        {
            throw new IOException($"The output path is an existing file: {m_destinationPath}");
        }

        string? backupPath = null;
        if (Directory.Exists(m_destinationPath))
        {
            if (requireMissingDestination)
                throw new IOException("An output directory appeared after publication was prepared.");
            backupPath = preparedBackupPath ?? Path.Combine(Path.GetDirectoryName(m_destinationPath)!, ".bgcs-backup-" + Guid.NewGuid().ToString("N"));
            OwnedPathBoundary.RejectPhysicalLinks(backupPath);
            DirectoryPublication.Move(m_destinationPath, backupPath, m_paths);
        }

        bool installed = false;
        try
        {
            DirectoryPublication.Move(stagingPath, m_destinationPath, m_paths);
            installed = true;
            m_boundary.Verify();
            m_committed = true;
        }
        catch (Exception installationFailure)
        {
            try
            {
                // Restore the captured physical owner even if its original alias
                // was redirected while installation was in progress.
                OwnedPathBoundary.RejectPhysicalLinks(m_destinationPath);
                OwnedPathBoundary.RejectPhysicalLinks(stagingPath);
                if (installed) DirectoryPublication.Move(m_destinationPath, stagingPath, m_paths);
                if (backupPath != null && !Directory.Exists(m_destinationPath) && Directory.Exists(backupPath))
                    DirectoryPublication.Move(backupPath, m_destinationPath, m_paths);
            }
            catch (Exception restorationFailure)
            {
                throw new AggregateException(
                    $"Output installation failed; the previous tree remains at '{backupPath}' and could not be restored to '{m_destinationPath}'.",
                    installationFailure, restorationFailure);
            }
            throw;
        }

        return backupPath;
    }

    internal void RestorePrevious(string? backupPath)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        OwnedPathBoundary.RejectPhysicalLinks(m_destinationPath);
        OwnedPathBoundary.RejectPhysicalLinks(stagingPath);
        if (backupPath is not null) OwnedPathBoundary.RejectPhysicalLinks(backupPath);
        if (!m_committed)
            throw new InvalidOperationException("No candidate has been installed by this transaction.");
        DirectoryPublication.Move(m_destinationPath, stagingPath, m_paths);
        m_committed = false;
        if (backupPath != null)
            DirectoryPublication.Move(backupPath, m_destinationPath, m_paths);
    }

    internal void RemoveBackup(string? backupPath)
    {
        if (backupPath == null)
            return;
        try
        {
            OwnedPathBoundary.RejectPhysicalLinks(backupPath);
            Directory.Delete(backupPath, recursive: true);
        }
        catch (Exception cleanupFailure) when (cleanupFailure is IOException or UnauthorizedAccessException)
        {
            throw new IOException(
                $"Output '{m_destinationPath}' was installed, but backup cleanup failed. The remaining backup is at '{backupPath}'.",
                cleanupFailure);
        }
    }

    /// <summary>
    /// Removes uncommitted staged output.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        try
        {
            if (!m_committed && Directory.Exists(stagingPath))
            {
                OwnedPathBoundary.RejectPhysicalLinks(stagingPath);
                Directory.Delete(stagingPath, recursive: true);
            }
        }
        finally
        {
            m_disposed = true;
            if (m_ownsLease)
                m_publicationLease.Dispose();
        }
    }
}
