using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using BGCS.Contracts.Inputs;
using BGCS.Infrastructure.IO;

namespace BGCS.Composition;

internal static class SourceSnapshotBatch
{
    internal static string? GetMismatch(
        IEnumerable<SourceSnapshot> snapshots,
        CancellationToken cancellationToken
    ) {
        // This set belongs to one verification fence, never an entire request.
        // Record equality includes both paths, byte length and content digest;
        // contradictory observations of the same file must still be checked.
        HashSet<InputDependency> checkedFiles = [];
        foreach (SourceSnapshot snapshot in snapshots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Validate the original collections before filtering can mask a
            // malformed default or null entry. Use the existing diagnostic.
            if (snapshot is null || snapshot.files.IsDefault || snapshot.searchDirectories.IsDefault
                || snapshot.excludedDirectories.IsDefault || snapshot.absentInputs.IsDefault
                || snapshot.files.Any(input => input is null) || snapshot.searchDirectories.Any(input => input is null)
                || snapshot.absentInputs.Any(input => input is null))
                return SourceSnapshotVerifier.GetMismatch(snapshot!, cancellationToken);
            SourceSnapshot remaining = snapshot with
            {
                files = snapshot.files.Where(checkedFiles.Add).ToImmutableArray()
            };
            // Keep absent-input, search-directory and exclusion semantics on
            // every original snapshot, including snapshots with no new files.
            if (SourceSnapshotVerifier.GetMismatch(remaining, cancellationToken) is string mismatch)
                return mismatch;
        }
        return null;
    }
}
