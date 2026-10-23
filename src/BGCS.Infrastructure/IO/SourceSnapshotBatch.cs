using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using BGCS.Core.Inputs;
using BGCS.Core.Hosting;

namespace BGCS.Infrastructure.IO;

/// <summary>Verifies snapshots at one input boundary while reading each exactly repeated dependency once.</summary>
/// <remarks>Every call creates an independent verification scope. No result is retained across compiler,
/// plugin or other external operations. File equality includes logical and physical paths, byte length
/// and digest. Directory equality also includes the complete ordered exclusion list; absence checks
/// and malformed collection checks remain obligations of every snapshot.</remarks>
public static class SourceSnapshotBatch
{
    /// <summary>Returns the first input mismatch while sharing exact file and directory checks within this call.</summary>
    /// <param name="snapshots">The snapshots in their required validation order.</param>
    /// <param name="paths">The explicitly selected host path identity service.</param>
    /// <param name="cancellationToken">Cancels dependency reads and traversal.</param>
    /// <returns>The first actionable mismatch, or null when all snapshots still match.</returns>
    /// <exception cref="ArgumentNullException">The snapshot sequence or one of its snapshots is null.</exception>
    /// <exception cref="OperationCanceledException">Cancellation is requested while processing the batch.</exception>
    public static string? GetMismatch(
        IEnumerable<SourceSnapshot> snapshots,
        IHostPathService paths,
        CancellationToken cancellationToken = default
    ) {
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(paths);
        List<PhysicalPathResolver.ResolutionScope> resolutions = [];
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
                return SourceSnapshotVerifier.GetMismatchWithinBoundary(snapshot!, snapshot!, paths, resolutions, cancellationToken);
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
            if (SourceSnapshotVerifier.GetMismatchWithinBoundary(remaining, snapshot, paths, resolutions, cancellationToken) is string mismatch)
                return mismatch;
        }
        return SourceSnapshotVerifier.CompleteBoundary(resolutions, paths, cancellationToken);
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
