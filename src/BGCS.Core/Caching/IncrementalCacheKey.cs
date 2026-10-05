using System;
using System.Linq;

namespace BGCS.Core.Caching;

/// <summary>
/// Identifies one generation by a normalized SHA-256 digest and the number of frozen source inputs.
/// </summary>
public sealed record IncrementalCacheKey
{
    /// <summary>
    /// Validates a content identity before it can select a cache directory.
    /// </summary>
    /// <param name="value">
    /// Exactly 64 lowercase hexadecimal characters, independent of output paths.
    /// </param>
    /// <param name="inputFileCount">
    /// The nonnegative number of distinct files included in the digest.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The digest is empty, malformed or not normalized.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The input count is negative.
    /// </exception>
    public IncrementalCacheKey(
        string value,
        int inputFileCount
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length != 64 || value.Any(static character => character is not
            (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("A cache identity must be a normalized SHA-256 digest.", nameof(value));
        ArgumentOutOfRangeException.ThrowIfNegative(inputFileCount);
        this.value = value;
        this.inputFileCount = inputFileCount;
    }

    /// <summary>
    /// Gets the normalized content identity used as the cache directory name.
    /// </summary>
    public string value { get; }

    /// <summary>
    /// Gets the number of distinct source inputs included in the identity.
    /// </summary>
    public int inputFileCount { get; }
}
