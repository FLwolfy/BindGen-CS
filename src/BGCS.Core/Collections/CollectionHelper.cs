using System;
using System.Collections.Generic;

namespace BGCS.Core.Collections;

/// <summary>
/// Adds sequences to mutable collections and reads optional lists without replacing their owner.
/// </summary>
public static class CollectionHelper
{
    /// <summary>
    /// Adds each source value using the destination set's equality comparer.
    /// </summary>
    /// <typeparam name="T">The type of values stored by the set.</typeparam>
    /// <param name="destination">The set to mutate.</param>
    /// <param name="values">The sequence to enumerate once; duplicate values are ignored.</param>
    /// <exception cref="ArgumentNullException">Either collection is null.</exception>
    public static void AddRange<T>(
        this HashSet<T> destination,
        IEnumerable<T> values
    ) {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(values);
        foreach (T value in values)
            destination.Add(value);
    }

    /// <summary>
    /// Copies entries into a destination dictionary, replacing values for existing keys.
    /// </summary>
    /// <typeparam name="TKey">The non-null key type.</typeparam>
    /// <typeparam name="TValue">The type of copied values.</typeparam>
    /// <param name="destination">The dictionary to mutate using its existing key comparer.</param>
    /// <param name="values">The dictionary whose entries are copied without removing other destination keys.</param>
    /// <exception cref="ArgumentNullException">Either dictionary is null.</exception>
    public static void AddRange<TKey, TValue>(
        this Dictionary<TKey, TValue> destination,
        Dictionary<TKey, TValue> values
    ) where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(values);
        foreach (KeyValuePair<TKey, TValue> pair in values)
            destination[pair.Key] = pair.Value;
    }

    /// <summary>
    /// Reads an indexed value when the list exists; absence of the list yields the type's default value.
    /// </summary>
    /// <typeparam name="T">The list element type.</typeparam>
    /// <param name="list">The list to read, or null when no collection is available.</param>
    /// <param name="index">The zero-based index, validated only when the list exists.</param>
    /// <returns>The indexed element, or default when the list is null.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A non-null list does not contain the requested index.</exception>
    public static T? Get<T>(
        this List<T>? list,
        int index
    ) => list is null ? default : list[index];
}
