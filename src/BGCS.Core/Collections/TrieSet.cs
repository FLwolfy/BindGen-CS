using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace BGCS.Core.Collections;

/// <summary>
/// Stores owned sequence keys in a prefix tree. Mutation and enumeration require external synchronization.
/// Internal nodes are private, and callers cannot mutate stored keys.
/// </summary>
/// <typeparam name="T">The non-null element used as an edge key.</typeparam>
public class TrieSet<T> : ICollection<IEnumerable<T>> where T : notnull
{
    private readonly IEqualityComparer<T> m_comparer;
    private readonly TrieNode m_root;
    private int m_count;

    /// <summary>
    /// Creates an empty tree using the default element comparer.
    /// </summary>
    public TrieSet() : this(EqualityComparer<T>.Default) { }

    /// <summary>
    /// Creates an empty tree using a comparer retained for its lifetime.
    /// </summary>
    /// <param name="comparer">The comparer used for all key elements.</param>
    /// <exception cref="ArgumentNullException">The comparer is null.</exception>
    public TrieSet(IEqualityComparer<T> comparer)
    {
        ArgumentNullException.ThrowIfNull(comparer);
        m_comparer = comparer;
        m_root = new TrieNode(default!, null, comparer);
    }

    /// <inheritdoc />
    public int Count => m_count;

    bool ICollection<IEnumerable<T>>.IsReadOnly => false;

    /// <summary>
    /// Copies a sequence into the tree. Later changes to the source collection do not change its key.
    /// </summary>
    /// <param name="value">The key to enumerate once and retain as an immutable sequence.</param>
    /// <exception cref="ArgumentNullException">The key is null.</exception>
    /// <exception cref="ArgumentException">An element is null.</exception>
    /// <exception cref="InvalidOperationException">An equal key is already stored.</exception>
    public void Add(IEnumerable<T> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        T[] snapshot = value.ToArray();
        if (snapshot.Any(static element => element is null))
            throw new ArgumentException("Trie keys cannot contain null elements.", nameof(value));
        TrieNode node = m_root;
        foreach (T key in snapshot)
        {
            if (!node.children.TryGetValue(key, out TrieNode? child))
            {
                child = new TrieNode(key, node, m_comparer);
                node.children.Add(key, child);
            }
            node = child;
        }
        if (node.value is not null)
            throw new InvalidOperationException("An equal key is already stored.");
        node.value = Array.AsReadOnly(snapshot);
        m_count++;
    }

    /// <summary>
    /// Adds keys in order. Earlier additions remain stored if a later addition fails.
    /// </summary>
    /// <param name="values">The collection of sequence keys.</param>
    /// <exception cref="ArgumentNullException">The collection or a key is null.</exception>
    /// <exception cref="InvalidOperationException">A key duplicates an existing or earlier key.</exception>
    public void AddRange(IEnumerable<IEnumerable<T>> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        foreach (IEnumerable<T> value in values)
            Add(value);
    }

    /// <inheritdoc />
    public void Clear()
    {
        m_root.children.Clear();
        m_root.value = null;
        m_count = 0;
    }

    /// <summary>
    /// Tests whether a complete sequence key is stored; an intermediate prefix is not a key.
    /// </summary>
    /// <param name="key">The sequence to locate.</param>
    /// <returns>True for a stored key, including a stored empty key; otherwise false.</returns>
    /// <exception cref="ArgumentNullException">The key is null.</exception>
    public bool Contains(IEnumerable<T> key) => GetNode(key)?.value is not null;

    /// <inheritdoc />
    public void CopyTo(
        IEnumerable<T>[] array,
        int arrayIndex
    ) {
        ArgumentNullException.ThrowIfNull(array);
        if (arrayIndex < 0 || arrayIndex > array.Length)
            throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        if (array.Length - arrayIndex < m_count)
            throw new ArgumentException("The destination cannot hold all stored keys.", nameof(array));
        foreach (IEnumerable<T> value in Enumerate(m_root))
            array[arrayIndex++] = value;
    }

    /// <summary>
    /// Removes one complete key and its unused suffix nodes, retaining longer stored keys.
    /// </summary>
    /// <param name="key">The sequence to remove.</param>
    /// <returns>True when a stored key was removed; false for an intermediate prefix or missing path.</returns>
    /// <exception cref="ArgumentNullException">The key is null.</exception>
    public bool Remove(IEnumerable<T> key)
    {
        TrieNode? node = GetNode(key);
        if (node?.value is null)
            return false;
        node.value = null;
        m_count--;
        while (node.parent is not null && node.value is null && node.children.Count == 0)
        {
            node.parent.children.Remove(node.key);
            node = node.parent;
        }
        return true;
    }

    /// <summary>
    /// Finds the longest complete stored prefix without allocating.
    /// </summary>
    /// <param name="match">The input span whose prefixes are tested.</param>
    /// <returns>A slice of the input; an empty slice when no non-empty stored key matches.</returns>
    public ReadOnlySpan<T> FindLargestMatch(ReadOnlySpan<T> match)
    {
        int matchedLength = 0;
        TrieNode node = m_root;
        for (int index = 0; index < match.Length; index++)
        {
            if (!node.children.TryGetValue(match[index], out TrieNode? child))
                break;
            node = child;
            if (node.value is not null)
                matchedLength = index + 1;
        }
        return match[..matchedLength];
    }

    /// <summary>
    /// Finds the shortest complete stored prefix, including an empty key when stored.
    /// </summary>
    /// <param name="match">The input span whose prefixes are tested.</param>
    /// <returns>A slice of the input; an empty slice for no match or a stored empty prefix.</returns>
    public ReadOnlySpan<T> FindSmallestMatch(ReadOnlySpan<T> match)
    {
        TrieNode node = m_root;
        if (node.value is not null)
            return match[..0];
        for (int index = 0; index < match.Length; index++)
        {
            if (!node.children.TryGetValue(match[index], out TrieNode? child))
                break;
            node = child;
            if (node.value is not null)
                return match[..(index + 1)];
        }
        return match[..0];
    }

    /// <summary>
    /// Enumerates immutable keys sharing a prefix. Do not mutate the tree during enumeration.
    /// </summary>
    /// <param name="prefix">The prefix to locate, or an empty sequence for all keys.</param>
    /// <returns>A lazy enumeration, empty when the prefix path is absent.</returns>
    /// <exception cref="ArgumentNullException">The prefix is null.</exception>
    public IEnumerable<IEnumerable<T>> GetByPrefix(IEnumerable<T> prefix)
    {
        TrieNode? node = GetNode(prefix);
        return node is null ? Enumerable.Empty<IEnumerable<T>>() : Enumerate(node);
    }

    /// <inheritdoc />
    public IEnumerator<IEnumerable<T>> GetEnumerator() => Enumerate(m_root).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private TrieNode? GetNode(IEnumerable<T> key)
    {
        ArgumentNullException.ThrowIfNull(key);
        TrieNode node = m_root;
        foreach (T element in key)
        {
            if (!node.children.TryGetValue(element, out TrieNode? child))
                return null;
            node = child;
        }
        return node;
    }

    private static IEnumerable<IEnumerable<T>> Enumerate(TrieNode start)
    {
        var pending = new Stack<TrieNode>();
        pending.Push(start);
        while (pending.TryPop(out TrieNode? node))
        {
            if (node.value is not null)
                yield return node.value;
            foreach (TrieNode child in node.children.Values)
                pending.Push(child);
        }
    }

    private sealed class TrieNode
    {
        public readonly T key;
        public readonly TrieNode? parent;
        public readonly Dictionary<T, TrieNode> children;
        public IReadOnlyList<T>? value;

        public TrieNode(
            T key,
            TrieNode? parent,
            IEqualityComparer<T> comparer
        ) {
            this.key = key;
            this.parent = parent;
            children = new Dictionary<T, TrieNode>(comparer);
        }
    }
}
