using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace BGCS.Core.Collections;

/// <summary>
/// Stores complete strings by character prefixes, reusing the owned sequence trie.
/// Mutation and enumeration require external synchronization.
/// </summary>
public class TrieStringSet : ICollection<string>
{
    private readonly TrieSet<char> m_keys;

    /// <summary>
    /// Creates an empty tree using ordinal character equality.
    /// </summary>
    public TrieStringSet() : this(EqualityComparer<char>.Default) { }

    /// <summary>
    /// Creates an empty tree using the supplied character comparer.
    /// </summary>
    /// <param name="comparer">The comparer retained for the tree's lifetime.</param>
    /// <exception cref="ArgumentNullException">The comparer is null.</exception>
    public TrieStringSet(IEqualityComparer<char> comparer) => m_keys = new TrieSet<char>(comparer);

    /// <inheritdoc />
    public int Count => m_keys.Count;

    bool ICollection<string>.IsReadOnly => false;

    /// <summary>
    /// Adds a complete string key, including an empty string.
    /// </summary>
    /// <param name="key">The key whose characters are retained.</param>
    /// <exception cref="ArgumentNullException">The key is null.</exception>
    /// <exception cref="InvalidOperationException">An equal key is already stored.</exception>
    public void Add(string key) => m_keys.Add(key);

    /// <summary>
    /// Adds keys in order; earlier additions remain stored if a later addition fails.
    /// </summary>
    /// <param name="keys">The string keys to enumerate and add.</param>
    /// <exception cref="ArgumentNullException">The collection or a key is null.</exception>
    /// <exception cref="InvalidOperationException">A key duplicates an existing or earlier key.</exception>
    public void AddRange(IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        foreach (string key in keys)
            Add(key);
    }

    /// <inheritdoc />
    public void Clear() => m_keys.Clear();

    /// <summary>
    /// Tests a complete key using the configured character comparer.
    /// </summary>
    /// <param name="key">The complete string to locate.</param>
    /// <returns>True for a stored key; false for an intermediate prefix or missing path.</returns>
    /// <exception cref="ArgumentNullException">The key is null.</exception>
    public bool Contains(string key) => m_keys.Contains(key);

    /// <inheritdoc />
    public void CopyTo(
        string[] array,
        int arrayIndex
    ) {
        ArgumentNullException.ThrowIfNull(array);
        if (arrayIndex < 0 || arrayIndex > array.Length)
            throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        if (array.Length - arrayIndex < Count)
            throw new ArgumentException("The destination cannot hold all stored keys.", nameof(array));
        foreach (string key in this)
            array[arrayIndex++] = key;
    }

    /// <summary>
    /// Removes one complete key while retaining longer keys sharing its prefix.
    /// </summary>
    /// <param name="key">The string key to remove.</param>
    /// <returns>True when a stored key was removed; otherwise false.</returns>
    /// <exception cref="ArgumentNullException">The key is null.</exception>
    public bool Remove(string key) => m_keys.Remove(key);

    /// <summary>
    /// Finds the longest complete stored prefix without allocating.
    /// </summary>
    /// <param name="match">The input span whose prefixes are tested.</param>
    /// <returns>A slice of the input; an empty slice when no non-empty key matches.</returns>
    public ReadOnlySpan<char> FindLargestMatch(ReadOnlySpan<char> match) => m_keys.FindLargestMatch(match);

    /// <summary>
    /// Finds the shortest complete stored prefix, including an empty key when stored.
    /// </summary>
    /// <param name="match">The input span whose prefixes are tested.</param>
    /// <returns>A slice of the input; an empty slice for no match or a stored empty prefix.</returns>
    public ReadOnlySpan<char> FindSmallestMatch(ReadOnlySpan<char> match) => m_keys.FindSmallestMatch(match);

    /// <summary>
    /// Enumerates complete keys sharing a prefix without exposing mutable tree nodes.
    /// </summary>
    /// <param name="prefix">The prefix to locate, or an empty string for all keys.</param>
    /// <returns>A lazy enumeration, empty when the prefix path is absent.</returns>
    /// <exception cref="ArgumentNullException">The prefix is null.</exception>
    public IEnumerable<string> GetByPrefix(string prefix) => m_keys.GetByPrefix(prefix).Select(ToStringKey);

    /// <inheritdoc />
    public IEnumerator<string> GetEnumerator() => m_keys.Select(ToStringKey).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private static string ToStringKey(IEnumerable<char> key) => new(key.ToArray());
}
