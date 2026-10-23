using BGCS.Core.Hosting;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace BGCS.Infrastructure.IO;

internal static class DirectoryPublication
{
    private static readonly TimeSpan RetryWindow = TimeSpan.FromSeconds(2);

    internal static void Move(
        string source,
        string destination,
        IHostPathService paths
    ) {
        long started = Stopwatch.GetTimestamp();
        while (true)
        {
            try
            {
                Directory.Move(source, destination);
                return;
            }
            catch (Exception exception) when (paths.ClassifyIoFailure(exception) is HostIoFailureKind.SharingViolation or HostIoFailureKind.TemporaryAccess
                && Stopwatch.GetElapsedTime(started) < RetryWindow)
            {
                // Temporary readers can deny rename even while publication ownership is held.
                // Preserve the original failure when the bounded window expires.
                Thread.Sleep(20);
            }
        }
    }


}
