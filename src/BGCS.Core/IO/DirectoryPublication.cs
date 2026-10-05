using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace BGCS.Core.IO;

internal static class DirectoryPublication
{
    private static readonly TimeSpan RetryWindow = TimeSpan.FromSeconds(2);

    internal static void Move(
        string source,
        string destination
    ) {
        long started = Stopwatch.GetTimestamp();
        while (true)
        {
            try
            {
                Directory.Move(source, destination);
                return;
            }
            catch (Exception exception) when (IsTemporaryAccessFailure(exception)
                && Stopwatch.GetElapsedTime(started) < RetryWindow)
            {
                // Temporary readers can deny rename even while publication ownership is held.
                // Preserve the original failure when the bounded window expires.
                Thread.Sleep(20);
            }
        }
    }

    private static bool IsTemporaryAccessFailure(Exception exception)
        => OperatingSystem.IsWindows()
            && exception is IOException or UnauthorizedAccessException
            && (exception.HResult & 0xffff) is 5 or 32 or 33;
}
