using System;
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
        HashSet<DirectoryQuery> checkedDirectories = new(new DirectoryQueryComparer());
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
                files = snapshot.files.Where(checkedFiles.Add).ToImmutableArray(),
                searchDirectories = snapshot.searchDirectories.Where(directory =>
                    checkedDirectories.Add(new(directory, snapshot.excludedDirectories))).ToImmutableArray()
            };
            // Query equality retains the entire expected record AND the ordered
            // exclusion list. A link may reach an exclusion outside the root, so
            // equal directory digests alone never justify merging those queries.
            // Every snapshot's absence checks and collection validation still run.
            if (SourceSnapshotVerifier.GetMismatch(remaining, cancellationToken) is string mismatch)
                return mismatch;
        }
        return null;
    }

    private readonly record struct DirectoryQuery(
        DirectoryInputDependency expected,
        ImmutableArray<string> exclusions
    );

    private sealed class DirectoryQueryComparer : IEqualityComparer<DirectoryQuery>
    {
        public bool Equals(
            DirectoryQuery left,
            DirectoryQuery right
        )
            => left.expected == right.expected && left.exclusions.SequenceEqual(right.exclusions, StringComparer.Ordinal);

        public int GetHashCode(DirectoryQuery query)
        {
            HashCode hash = new();
            hash.Add(query.expected);
            foreach (string exclusion in query.exclusions) hash.Add(exclusion, StringComparer.Ordinal);
            return hash.ToHashCode();
        }
    }
}
