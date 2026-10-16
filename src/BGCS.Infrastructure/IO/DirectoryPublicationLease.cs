using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace BGCS.Infrastructure.IO;

internal static class DirectoryPublicationLease
{
    /// <summary>
    /// Resolves the retained lock inode beside a publication directory.
    /// </summary>
    internal static string GetPath(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        string destination = new OwnedPathBoundary(directory).physicalPath;
        string parent = Path.GetDirectoryName(destination)
            ?? throw new ArgumentException("The output path must have a parent directory.", nameof(directory));
        return Path.Combine(parent, "." + Path.GetFileName(destination) + ".bgcs-lock");
    }

    /// <summary>
    /// Acquires process-shared publication ownership; the caller releases the stream without unlinking the lock file.
    /// </summary>
    internal static FileStream Acquire(
        string directory,
        TimeSpan timeout,
        CancellationToken cancellationToken
    ) {
        if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        cancellationToken.ThrowIfCancellationRequested();
        OwnedPathBoundary boundary = new(directory);
        string path = GetPath(boundary.physicalPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        long started = Stopwatch.GetTimestamp();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            boundary.Verify();
            OwnedPathBoundary.RejectPhysicalLinks(path);
            try
            {
                // Retain the inode after release so every process continues to contend for the same lock.
                FileStream lease = new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                try
                {
                    boundary.Verify();
                    OwnedPathBoundary.RejectPhysicalLinks(path);
                    cancellationToken.ThrowIfCancellationRequested();
                    return lease;
                }
                catch { lease.Dispose(); throw; }
            }
            catch (IOException exception) when (IsSharingViolation(exception))
            {
                if (timeout != Timeout.InfiniteTimeSpan && Stopwatch.GetElapsedTime(started) >= timeout)
                    throw new TimeoutException($"Another publisher retains output ownership at '{directory}'.", exception);
                if (cancellationToken.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(20)))
                    cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    private static bool IsSharingViolation(IOException exception)
    {
        int code = exception.HResult & 0xffff;
        // Unix FileShare.None exposes the native flock errno, which differs by host.
        return code is 32 or 33 || code == (OperatingSystem.IsMacOS() ? 35 : 11);
    }
}
