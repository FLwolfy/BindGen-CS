using System;
using System.Collections;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BGCS.Core.IO;

/// <summary>
/// Captures an immutable set of absolute source paths for one declaration analysis boundary.
/// </summary>
/// <remarks>
/// Relative entries are resolved during construction. Membership queries resolve relative paths
/// against the current working directory. Windows paths use case-insensitive comparison.
/// </remarks>
public sealed class FileSet : IReadOnlySet<string>
{
    private readonly FrozenSet<string> m_paths;

    /// <summary>
    /// Copies and normalizes the selected paths without retaining the caller's collection.
    /// </summary>
    /// <param name="paths">
    /// Source file paths; duplicates collapse after absolute path normalization.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// The collection is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// An entry is empty or is not a valid path.
    /// </exception>
    public FileSet(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        m_paths = Normalize(paths).ToFrozenSet(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public int Count => m_paths.Count;

    /// <summary>
    /// Checks whether a normalized path belongs to the captured source boundary.
    /// </summary>
    /// <param name="path">
    /// An absolute or relative source path; null and empty values are treated as absent.
    /// </param>
    /// <returns>
    /// True when the absolute path matches a captured entry; otherwise false.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// A nonempty path cannot be normalized.
    /// </exception>
    public bool Contains(string path) => !string.IsNullOrEmpty(path) && m_paths.Contains(Path.GetFullPath(path));

    /// <inheritdoc />
    public bool IsProperSubsetOf(IEnumerable<string> other) => m_paths.IsProperSubsetOf(Normalize(other));

    /// <inheritdoc />
    public bool IsProperSupersetOf(IEnumerable<string> other) => m_paths.IsProperSupersetOf(Normalize(other));

    /// <inheritdoc />
    public bool IsSubsetOf(IEnumerable<string> other) => m_paths.IsSubsetOf(Normalize(other));

    /// <inheritdoc />
    public bool IsSupersetOf(IEnumerable<string> other) => m_paths.IsSupersetOf(Normalize(other));

    /// <inheritdoc />
    public bool Overlaps(IEnumerable<string> other) => m_paths.Overlaps(Normalize(other));

    /// <inheritdoc />
    public bool SetEquals(IEnumerable<string> other) => m_paths.SetEquals(Normalize(other));

    /// <inheritdoc />
    public IEnumerator<string> GetEnumerator() => m_paths.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private static IEnumerable<string> Normalize(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return paths.Select(static path =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            return Path.GetFullPath(path);
        });
    }
}
