using System;
using System.IO;

namespace BGCS.Infrastructure.IO;

internal static class OwnedFileCleanup
{
    internal static void Delete(
        string? path,
        Exception? originalFailure
    ) {
        if (path is null) return;
        try
        {
            // A rejected post-write boundary can also invalidate the pending
            // file's physical parents. Never follow those links during cleanup.
            OwnedPathBoundary.RejectPhysicalLinks(path);
            File.Delete(path);
        }
        catch (Exception cleanupFailure)
        {
            if (originalFailure is not null)
                throw new AggregateException("The operation failed and its pending owned file could not be safely removed.", originalFailure, cleanupFailure);
            throw;
        }
    }
}
