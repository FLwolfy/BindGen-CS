using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Inputs;
using BGCS.Core.Hosting;

namespace BGCS.Infrastructure.IO;

/// <summary>Rechecks a saved parse-input snapshot before generated models are reused.</summary>
public static class SourceSnapshotVerifier
{
    /// <summary>Checks actual file bytes, link identities and complete include-search membership.</summary>
    /// <param name="snapshot">The dependency snapshot from the original frontend result.</param>
    /// <param name="paths">The explicitly selected host path identity service.</param>
    /// <param name="cancellationToken">Cancels file and search-directory verification.</param>
    /// <returns>False for missing, changed, redirected or unreadable inputs.</returns>
    public static bool Matches(
        SourceSnapshot snapshot,
        IHostPathService paths,
        CancellationToken cancellationToken = default
    )
        => GetMismatch(snapshot, paths, cancellationToken) is null;

    /// <summary>Identifies the first stale or unreadable dependency without using timestamps as content proof.</summary>
    /// <param name="snapshot">The detached file and search snapshot.</param>
    /// <param name="paths">The explicitly selected host path identity service.</param>
    /// <param name="cancellationToken">Cancels verification.</param>
    /// <returns>An actionable mismatch description, or null when every dependency still matches.</returns>
    public static string? GetMismatch(
        SourceSnapshot snapshot,
        IHostPathService paths,
        CancellationToken cancellationToken = default
    ) {
        List<PhysicalPathResolver.ResolutionScope> resolutions = [];
        return GetMismatchWithinBoundary(snapshot, snapshot, paths, resolutions, cancellationToken)
            ?? CompleteBoundary(resolutions, paths, cancellationToken);
    }

    internal static string? GetMismatchWithinBoundary(
        SourceSnapshot snapshot,
        SourceSnapshot comparisonInputs,
        IHostPathService paths,
        ICollection<PhysicalPathResolver.ResolutionScope> resolutions,
        CancellationToken cancellationToken
    ) {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(paths);
        if (snapshot.files.IsDefault || snapshot.searchDirectories.IsDefault || snapshot.excludedDirectories.IsDefault || snapshot.absentInputs.IsDefault
            || snapshot.files.Any(input => input is null) || snapshot.searchDirectories.Any(input => input is null) || snapshot.absentInputs.Any(input => input is null))
            return "The input snapshot contains an uninitialized or null dependency collection.";
        try
        {
            foreach (MissingInputDependency input in snapshot.absentInputs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(input.path) || Directory.Exists(input.path) || PhysicalPathResolver.Resolve(input.path, paths) != input.resolvedPath)
                    return "An absent input was created or redirected: " + input.path;
            }
            bool parallel = snapshot.files.Length >= 64 || snapshot.files.Any(dependency => dependency.length >= 8388608);
            using ThreadLocal<PhysicalPathResolver.ResolutionScope>? parallelPaths = parallel
                ? new(() => new(paths), trackAllValues: true) : null;
            PhysicalPathResolver.ResolutionScope? sequentialPaths = !parallel && !snapshot.files.IsEmpty ? new(paths) : null;
            if (parallel)
            {
                string?[] mismatches = new string?[snapshot.files.Length];
                ExceptionDispatchInfo?[] failures = new ExceptionDispatchInfo?[snapshot.files.Length];
                Parallel.For(0, snapshot.files.Length, new ParallelOptions
                {
                    MaxDegreeOfParallelism = 2,
                    CancellationToken = cancellationToken,
                    TaskScheduler = TaskScheduler.Default
                }, index =>
                {
                    try { mismatches[index] = FileMismatch(snapshot.files[index], parallelPaths!.Value!, cancellationToken); }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                    catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
                    { mismatches[index] = "An input dependency could not be read: " + failure.Message; }
                    catch (Exception failure) { failures[index] = ExceptionDispatchInfo.Capture(failure); }
                });
                // Completion order must not choose a different diagnostic or hide
                // an earlier malformed dependency behind a later file mismatch.
                for (int index = 0; index < snapshot.files.Length; index++)
                {
                    if (mismatches[index] is string mismatch) return mismatch;
                    failures[index]?.Throw();
                }
            }
            else
                foreach (InputDependency dependency in snapshot.files)
                    if (FileMismatch(dependency, sequentialPaths!, cancellationToken) is string mismatch) return mismatch;
            foreach (DirectoryInputDependency directory in snapshot.searchDirectories)
                if (directory != DirectoryMembershipDigest.CaptureWithinBoundary(directory.path, paths, snapshot.excludedDirectories, cancellationToken))
                    return "Include or library search membership changed: " + directory.path;
            // Dependency reads may be deduplicated by a batch, but metadata must
            // cover every original input rather than only the remaining reads.
            if (InputPathSemanticsSnapshot.GetMismatchWithinBoundary(comparisonInputs, paths) is string semanticsMismatch) return semanticsMismatch;
            // The containing synchronous fence closes each thread's independent
            // observations after every participating snapshot has been checked.
            if (parallelPaths is not null)
                foreach (PhysicalPathResolver.ResolutionScope resolution in parallelPaths.Values) resolutions.Add(resolution);
            else if (sequentialPaths is not null) resolutions.Add(sequentialPaths);
            return null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return "An input dependency could not be read: " + failure.Message;
        }
    }

    internal static string? CompleteBoundary(
        IEnumerable<PhysicalPathResolver.ResolutionScope> resolutions,
        IHostPathService paths,
        CancellationToken cancellationToken
    ) {
        try
        {
            foreach (PhysicalPathResolver.ResolutionScope resolution in resolutions) resolution.Verify(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            paths.Verify();
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return "An input dependency could not be read: " + failure.Message;
        }
    }

    private static string? FileMismatch(
        InputDependency dependency,
        PhysicalPathResolver.ResolutionScope paths,
        CancellationToken cancellationToken
    ) {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(dependency.path) || !string.Equals(dependency.resolvedPath, paths.Resolve(dependency.path, cancellationToken), StringComparison.Ordinal))
            return "An input file disappeared or was redirected: " + dependency.path;
        using FileStream file = new(dependency.path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.SequentialScan);
        if (file.Length != dependency.length || !string.Equals(dependency.digest.value, Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant(), StringComparison.Ordinal))
            return "Input file bytes changed: " + dependency.path;
        return null;
    }
}
