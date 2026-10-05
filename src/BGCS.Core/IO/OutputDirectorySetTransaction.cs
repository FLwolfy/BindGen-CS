using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace BGCS.Core.IO;

/// <summary>
/// Publishes related output directories while retaining all previous trees until installation completes.
/// </summary>
/// <remarks>
/// Readers participate in the same directory publication ownership. Filesystems do not provide a
/// simultaneous rename across directories; failed installation rolls back every previously installed tree.
/// </remarks>
public sealed class OutputDirectorySetTransaction : IDisposable
{
    private readonly SortedDictionary<string, OutputDirectoryTransaction> m_transactions;
    private readonly IReadOnlyDictionary<string, string> m_stagingPaths;
    private bool m_committed;
    private bool m_faulted;
    private bool m_disposed;

    /// <summary>
    /// Acquires all output owners in canonical path order under one shared deadline.
    /// </summary>
    /// <param name="destinationPaths">Distinct output directories; none may contain another.</param>
    /// <param name="lockTimeout">Maximum combined ownership wait; null uses five minutes.</param>
    /// <param name="cancellationToken">Cancels ownership acquisition before any output is replaced.</param>
    /// <exception cref="ArgumentNullException">The destination sequence is null.</exception>
    /// <exception cref="ArgumentException">Paths are empty, repeat or overlap.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The timeout is negative and not infinite.</exception>
    /// <exception cref="TimeoutException">An output owner exceeds the shared deadline.</exception>
    /// <exception cref="OperationCanceledException">Ownership acquisition is canceled.</exception>
    public OutputDirectorySetTransaction(
        IEnumerable<string> destinationPaths,
        TimeSpan? lockTimeout = null,
        CancellationToken cancellationToken = default
    ) {
        ArgumentNullException.ThrowIfNull(destinationPaths);
        TimeSpan timeout = lockTimeout ?? TimeSpan.FromMinutes(5);
        if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(lockTimeout));
        StringComparer comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        string[] paths = destinationPaths.Select(NormalizePath).OrderBy(path => path, comparer).ToArray();
        ValidatePaths(paths, comparer);
        m_transactions = new(comparer);
        Stopwatch elapsed = Stopwatch.StartNew();
        try
        {
            foreach (string path in paths)
            {
                TimeSpan remaining = timeout == Timeout.InfiniteTimeSpan ? timeout : timeout - elapsed.Elapsed;
                if (remaining < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
                    remaining = TimeSpan.Zero;
                m_transactions.Add(path, new(path, remaining, cancellationToken));
            }
        }
        catch (Exception acquisitionFailure)
        {
            try
            {
                Dispose();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException("Output ownership acquisition and candidate cleanup failed.", acquisitionFailure, cleanupFailure);
            }
            throw;
        }
        m_stagingPaths = new ReadOnlyDictionary<string, string>(m_transactions.ToDictionary(
            entry => entry.Key, entry => entry.Value.stagingPath, comparer));
    }

    /// <summary>
    /// Gets the canonical destination-to-candidate map owned by this transaction.
    /// </summary>
    public IReadOnlyDictionary<string, string> stagingPaths => m_stagingPaths;

    /// <summary>
    /// Resolves the candidate directory for one participating destination.
    /// </summary>
    /// <param name="destinationPath">An output path supplied when this transaction was created.</param>
    /// <returns>The private staging directory that will replace this destination on commit.</returns>
    /// <exception cref="ArgumentException">The path does not participate in this transaction.</exception>
    /// <exception cref="ObjectDisposedException">Ownership has already been released.</exception>
    public string GetStagingPath(string destinationPath)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        return m_stagingPaths.TryGetValue(NormalizePath(destinationPath), out string? path)
            ? path : throw new ArgumentException("The output path does not participate in this transaction.", nameof(destinationPath));
    }

    /// <summary>
    /// Installs all candidates and removes previous trees only after the complete set is installed.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Ownership has already been released.</exception>
    /// <exception cref="InvalidOperationException">The transaction has committed or a prior commit failed.</exception>
    /// <exception cref="IOException">Installation fails, or committed backup cleanup fails.</exception>
    /// <exception cref="UnauthorizedAccessException">A directory move remains denied after its bounded retry window.</exception>
    /// <exception cref="AggregateException">Installation and rollback both fail, or multiple backup removals fail.</exception>
    public void Commit()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (m_committed || m_faulted)
            throw new InvalidOperationException("An output set can attempt publication only once.");
        List<(OutputDirectoryTransaction transaction, string? backup)> installed = [];
        try
        {
            foreach (OutputDirectoryTransaction transaction in m_transactions.Values)
                installed.Add((transaction, transaction.InstallCandidate()));
        }
        catch (Exception installationFailure)
        {
            m_faulted = true;
            List<Exception> failures = [installationFailure];
            for (int index = installed.Count - 1; index >= 0; index--)
            {
                try
                {
                    installed[index].transaction.RestorePrevious(installed[index].backup);
                }
                catch (Exception rollbackFailure)
                {
                    failures.Add(new IOException($"Could not restore previous output from '{installed[index].backup}'.", rollbackFailure));
                }
            }
            if (failures.Count > 1)
                throw new AggregateException("Output set installation failed and previous trees could not all be restored.", failures);
            throw;
        }
        m_committed = true;
        List<Exception> cleanupFailures = [];
        foreach ((OutputDirectoryTransaction transaction, string? backup) in installed)
        {
            try
            {
                transaction.RemoveBackup(backup);
            }
            catch (Exception cleanupFailure)
            {
                cleanupFailures.Add(cleanupFailure);
            }
        }
        if (cleanupFailures.Count > 0)
            throw new AggregateException("The complete output set was installed, but backup cleanup failed.", cleanupFailures);
    }

    /// <summary>
    /// Removes remaining candidates and releases every publication owner, including after a failed commit.
    /// </summary>
    /// <exception cref="AggregateException">One or more candidate directories could not be removed.</exception>
    public void Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        List<Exception> failures = [];
        foreach (OutputDirectoryTransaction transaction in m_transactions.Values.Reverse())
        {
            try
            {
                transaction.Dispose();
            }
            catch (Exception failure)
            {
                failures.Add(failure);
            }
        }
        if (failures.Count > 0)
            throw new AggregateException("Output candidate cleanup failed; publication ownership has been released.", failures);
    }

    private static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (Path.GetDirectoryName(fullPath) == null)
            throw new ArgumentException("An output path must have a parent directory.", nameof(path));
        return fullPath;
    }

    private static void ValidatePaths(
        IReadOnlyList<string> paths,
        StringComparer comparer
    ) {
        if (paths.Count == 0)
            throw new ArgumentException("At least one output path is required.", nameof(paths));
        for (int index = 0; index < paths.Count; index++)
        {
            for (int previous = 0; previous < index; previous++)
            {
                string relative = Path.GetRelativePath(paths[previous], paths[index]);
                if (comparer.Equals(paths[previous], paths[index])
                    || !Path.IsPathRooted(relative) && relative != ".."
                        && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    throw new ArgumentException($"Output directories repeat or overlap: '{paths[previous]}' and '{paths[index]}'.", nameof(paths));
                }
            }
        }
    }
}
