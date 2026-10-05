using System;
using System.Collections.Generic;

namespace BGCS.Core.Collections;

/// <summary>
/// Provides indexed lookup without copying read-only analysis collections.
/// </summary>
public static class ReadOnlyListExtensions
{
    /// <summary>
    /// Finds the first element equal to a supplied value using the default equality comparer.
    /// </summary>
    /// <typeparam name = "T">
    /// The element type.
    /// </typeparam>
    /// <param name = "values">
    /// The collection to search; ownership remains with the caller.
    /// </param>
    /// <param name = "value">
    /// The element to locate.
    /// </param>
    /// <returns>
    /// The zero-based position, or minus one if the value is absent.
    /// </returns>
    /// <exception cref = "ArgumentNullException">
    /// The collection is null.
    /// </exception>
    public static int IndexOf<T>(
        this IReadOnlyList<T> values,
        T value
    ) {
        ArgumentNullException.ThrowIfNull(values);
        EqualityComparer<T> comparer = EqualityComparer<T>.Default;
        for (int index = 0; index < values.Count; index++)
        {
            if (comparer.Equals(values[index], value))
                return index;
        }

        return -1;
    }
}
