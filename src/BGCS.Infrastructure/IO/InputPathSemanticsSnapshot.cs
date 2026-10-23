using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using BGCS.Core.Hosting;
using BGCS.Core.Inputs;

namespace BGCS.Infrastructure.IO;

/// <summary>Captures host comparison metadata only for declared input paths, independently of ephemeral output operations.</summary>
public static class InputPathSemanticsSnapshot
{
    /// <summary>Freezes every effective parent comparison fact needed to interpret the snapshot's input names.</summary>
    /// <param name="snapshot">The complete captured input and search records.</param>
    /// <param name="paths">The explicit host path observer.</param>
    /// <returns>The same detached inputs with a complete current metadata capture.</returns>
    public static SourceSnapshot Capture(
        SourceSnapshot snapshot,
        IHostPathService paths
    ) {
        SourceSnapshot captured = CaptureWithinBoundary(snapshot, paths);
        paths.Verify();
        return captured;
    }

    internal static SourceSnapshot CaptureWithinBoundary(
        SourceSnapshot snapshot,
        IHostPathService paths
    ) {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(paths);
        ImmutableArray<InputPathSemantics> observed = Directories(snapshot)
            .Select(directory => new InputPathSemantics(directory, paths.GetSemantics(directory).caseSensitive)).ToImmutableArray();
        return snapshot with { pathSemantics = observed };
    }

    /// <summary>Validates coverage and rechecks captured input-only directory comparison facts.</summary>
    /// <param name="snapshot">The detached snapshot to verify.</param>
    /// <param name="paths">The selected explicit host observer.</param>
    /// <returns>The first missing, duplicated or changed metadata fact, or null.</returns>
    public static string? GetMismatch(
        SourceSnapshot snapshot,
        IHostPathService paths
    ) {
        string? mismatch = GetMismatchWithinBoundary(snapshot, paths);
        if (mismatch is null) paths.Verify();
        return mismatch;
    }

    internal static string? GetMismatchWithinBoundary(
        SourceSnapshot snapshot,
        IHostPathService paths
    ) {
        if (GetCoverageMismatch(snapshot) is string mismatch) return mismatch;
        foreach (InputPathSemantics item in snapshot.pathSemantics)
            if (paths.GetSemantics(item.directory).caseSensitive != item.caseSensitive)
                return "Input directory name-comparison semantics changed: " + item.directory;
        return null;
    }

    private static string? GetCoverageMismatch(SourceSnapshot snapshot)
    {
        if (snapshot.pathSemantics.IsDefault || snapshot.pathSemantics.Any(item => item is null || !Path.IsPathFullyQualified(item.directory)))
            return "The input snapshot contains missing or invalid host path semantics.";
        Dictionary<string, InputPathSemantics> expected = new(StringComparer.Ordinal);
        foreach (InputPathSemantics item in snapshot.pathSemantics)
            if (!expected.TryAdd(item.directory, item)) return "The input snapshot repeats host path semantics: " + item.directory;
        foreach (string directory in Directories(snapshot))
            if (!expected.ContainsKey(directory)) return "The input snapshot omitted host path semantics: " + directory;
        return null;
    }

    /// <summary>Projects comparison facts needed by declared content and searches for cache identities.</summary>
    /// <param name="snapshot">The independently verified complete snapshot.</param>
    /// <returns>Captured input-name semantics without unrelated operational observations.</returns>
    /// <remarks>This is an identity projection, not a replacement for full snapshot verification before publication.</remarks>
    public static ImmutableArray<InputPathSemantics> ForInputIdentity(SourceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        HashSet<string> required = Directories(snapshot).ToHashSet(StringComparer.Ordinal);
        return snapshot.pathSemantics.Where(item => required.Contains(item.directory)).ToImmutableArray();
    }

    private static IEnumerable<string> Directories(SourceSnapshot snapshot)
    {
        HashSet<string> directories = new(StringComparer.Ordinal);
        foreach (string file in snapshot.files.SelectMany(input => new[] { input.path, input.resolvedPath })
            .Concat(snapshot.absentInputs.SelectMany(input => new[] { input.path, input.resolvedPath })))
            Add(Path.GetDirectoryName(Absolute(file))!);
        // Exclusions are operational fences, not content or search inputs. Their
        // live observations remain checked by paths.Verify and publication
        // ownership checks, without making input metadata session-specific.
        foreach (string directory in snapshot.searchDirectories.SelectMany(input => new[] { input.path, input.resolvedPath })) Add(Absolute(directory));
        return directories.OrderBy(directory => directory, StringComparer.Ordinal);

        void Add(string directory)
        {
            for (string? current = directory; current is not null; current = Path.GetDirectoryName(current))
                directories.Add(current);
        }
        static string Absolute(string path) => Path.IsPathFullyQualified(path) ? Path.GetFullPath(path)
            : throw new ArgumentException("Snapshot path semantics require captured absolute input names.");
    }
}
