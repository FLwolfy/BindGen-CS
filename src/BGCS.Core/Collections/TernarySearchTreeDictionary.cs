using System;
// Ternary Search Tree Implementation for C#
//
// Rewritten by Eric Domke
//
// Code adapted from implementation by Jonathan de Halleux
//   at http://www.codeproject.com/Articles/5819/Ternary-Search-Tree-Dictionary-in-C-Faster-String
//
// Rewrite focused on
// - removing fields from the TstDictionaryEntry class to reduce memory usage
// - decreasing the number of nodes to reduce memory usage (used some of the
//   ideas from http://hackthology.com/ternary-search-tries-for-fast-flexible-string-search-part-1.html)
// - implementing the modern IDictionary<string, T> interface
// - supporting case-insensitive matching and retrieval
// - supporting "starts with" style searching of the tree
// - adding utilities for creating a balanced tree using the algorithm at
//   http://www.drdobbs.com/database/ternary-search-trees/184410528?pgno=2
// - removing unnecessary public members
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace BGCS.Core.Collections;

/// <summary>
/// Stores nonempty string keys in a compressed ternary search tree and supports comparer-aware exact and prefix lookup.
/// </summary>
/// <typeparam name="T">
/// The value stored for each distinct key.
/// </typeparam>
public class TernarySearchTreeDictionary<T> : IDictionary<string, T>, ICollection, IReadOnlyDictionary<string, T>
{
    private readonly Func<char, char, int> m_compare;
    private readonly IComparer<string> m_comparer;
    private TstDictionaryEntry<T>? m_root;
    private long m_version;
    /// <summary>
    /// Constructor
    /// </summary>
    /// <remarks>
    /// Construct an empty ternary search tree with an ordinal comparer
    /// </remarks>
    public TernarySearchTreeDictionary() : this(StringComparer.Ordinal)
    {
    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name = "comparer">Comparer used to compare keys</param>
    /// <remarks>
    /// Construct an empty ternary search tree with the specified comparer
    /// </remarks>
    public TernarySearchTreeDictionary(IComparer<string> comparer)
    {
        ArgumentNullException.ThrowIfNull(comparer);
        this.m_root = null;
        this.m_version = 0;
        this.m_comparer = comparer;
        if (this.m_comparer == StringComparer.Ordinal)
        {
            this.m_compare = (
                x,
                y
            ) => x - y;
        }
        else if (this.m_comparer == StringComparer.OrdinalIgnoreCase)
        {
            this.m_compare = (
                x,
                y
            ) => char.ToUpperInvariant(x) - char.ToUpperInvariant(y);
        }
        else
        {
            this.m_compare = (
                x,
                y
            ) => this.m_comparer.Compare(x.ToString(), y.ToString());
        }
    }

    /// <summary>
    /// Constructor that adds multiple elements into the <see cref="TernarySearchTreeDictionary{T}"/>
    /// </summary>
    /// <param name = "values">The elements to add</param>
    /// <remarks>
    /// Construct and populate a ternary search tree.
    /// </remarks>
    public TernarySearchTreeDictionary(IEnumerable<KeyValuePair<string, T>> values) : this(values, StringComparer.Ordinal)
    {
    }

    /// <summary>
    /// Constructor that adds multiple elements into the <see cref="TernarySearchTreeDictionary{T}"/>
    /// </summary>
    /// <param name = "values">The elements to add</param>
    /// <param name = "comparer">Comparer used to compare keys</param>
    /// <remarks>
    /// Construct and populate a ternary search tree.
    /// </remarks>
    public TernarySearchTreeDictionary(
        IEnumerable<KeyValuePair<string, T>> values,
        IComparer<string> comparer
    ) : this(comparer)
    {
        AddRange(values);
    }

    /// <summary>
    /// Returns the comparer used to compare keys in the <see cref="TernarySearchTreeDictionary{T}"/>
    /// </summary>
    public IComparer<string>? comparer
    {
        get
        {
            return this.m_comparer;
        }
    }

    /// <summary>
    /// Gets the number of key-and-_value pairs contained in the <see cref="TernarySearchTreeDictionary{T}"/>.
    /// </summary>
    /// <value>
    /// The number of key-and-_value pairs contained in the <see cref="TernarySearchTreeDictionary{T}"/>.
    /// </value>
    /// <remarks>
    /// Complexity: O(N)
    /// </remarks>
    public virtual int Count
    {
        get
        {
            var en = GetEnumerator();
            int n = 0;
            while (en.MoveNext())
            {
                ++n;
            }

            return n;
        }
    }

    /// <summary>
    /// Gets an <see cref = "ICollection"/> containing the keys in the <see cref="TernarySearchTreeDictionary{T}"/>.
    /// </summary>
    /// <returns>
    /// An <see cref = "ICollection"/> containing the keys in the <see cref="TernarySearchTreeDictionary{T}"/>.
    /// </returns>
    public virtual ICollection<string> Keys
    {
        get
        {
            var keys = new List<string>();
            foreach (var kvp in this)
            {
                keys.Add(kvp.Key);
            }

            return keys;
        }
    }

    /// <summary>
    /// Gets an <see cref = "ICollection"/> containing the values in the <see cref="TernarySearchTreeDictionary{T}"/>.
    /// </summary>
    /// <returns>
    /// An <see cref = "ICollection"/> containing the values in the <see cref="TernarySearchTreeDictionary{T}"/>.
    /// </returns>
    public virtual ICollection<T> Values
    {
        get
        {
            var values = new List<T>();
            foreach (var kvp in this)
            {
                values.Add(kvp.Value);
            }

            return values;
        }
    }

    /// <summary>
    /// Gets the value for an exact key or replaces that value; assigning an absent key adds a new entry.
    /// </summary>
    /// <param name="key">
    /// The nonempty key compared using the configured comparer.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// The key is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The key is empty.
    /// </exception>
    /// <exception cref="KeyNotFoundException">
    /// The getter cannot find an exact key.
    /// </exception>
    public virtual T this[string key]
    {
        get
        {
            if (!TryGetNode(key, null, out TernarySearchTreeDictionary<T>.TstDictionaryEntry<T>? entry))
            {
                throw new KeyNotFoundException();
            }

            if (entry == null)
            {
                throw new KeyNotFoundException();
            }

            return entry.value.Value;
        }

        set
        {
            if (key == null)
            {
                throw new ArgumentNullException("key");
            }

            if (key.Length == 0)
            {
                throw new ArgumentException("key is an empty string");
            }

            // updating version
            ++this.m_version;
            var de = Find(key);
            if (de == null)
            {
                Add(key, value);
            }
            else
            {
                de.value = new KeyValuePair<string, T>(key, value);
            }
        }
    }

    /// <summary>
    /// Adds an element with the specified key and _value into the <see cref="TernarySearchTreeDictionary{T}"/>.
    /// </summary>
    /// <param name = "key">The key of the element to add.</param>
    /// <param name = "value">The _value of the element to add. The _value can be a null reference (Nothing in Visual Basic).</param>
    /// <exception cref = "ArgumentNullException">
    /// <paramref name = "key"/> is a null reference (Nothing in Visual Basic).
    /// </exception>
    /// <exception cref = "ArgumentException"><paramref name = "key"/> is an empty string</exception>
    /// <exception cref = "ArgumentException">
    /// An element with the same key already exists in the <see cref="TernarySearchTreeDictionary{T}"/>.
    /// </exception>
    /// <exception cref = "NotSupportedException">The <see cref="TernarySearchTreeDictionary{T}"/> is read-only.</exception>
    /// <exception cref = "NotSupportedException">The <see cref="TernarySearchTreeDictionary{T}"/> has a fixed size.</exception>
    public virtual void Add(
        string key,
        T value
    ) {
        Add(new KeyValuePair<string, T>(key, value));
    }

    /// <summary>
    /// Adds an element with the specified key and _value into the <see cref="TernarySearchTreeDictionary{T}"/>.
    /// </summary>
    /// <param name = "item">The element to add.</param>
    /// <exception cref = "ArgumentNullException">
    /// <paramref name = "item"/>.<c>Key</c> is a null reference (Nothing in Visual Basic).
    /// </exception>
    /// <exception cref = "ArgumentException"><paramref name = "item"/>.<c>Key</c> is an empty string</exception>
    /// <exception cref = "ArgumentException">
    /// An element with the same key already exists in the <see cref="TernarySearchTreeDictionary{T}"/>.
    /// </exception>
    public void Add(KeyValuePair<string, T> item)
    {
        if (item.Key == null)
        {
            throw new ArgumentNullException("key is null");
        }

        if (item.Key.Length == 0)
        {
            throw new ArgumentException("trying to add empty key");
        }

        // updating version
        ++this.m_version;
        // creating root node if needed.
        if (this.m_root == null)
        {
            this.m_root = new TstDictionaryEntry<T>(item.Key[0]);
            this.m_root.value = item;
            return;
        }

        // adding key
        var p = this.m_root;
        int i = 0;
        char c;
        while (i <= item.Key.Length)
        {
            c = i < item.Key.Length ? item.Key[i] : '\0';
            var cmp = this.m_compare(c, p.splitChar);
            if (cmp < 0)
            {
                if (p.lowChild == null)
                {
                    p.lowChild = new TstDictionaryEntry<T>(c);
                    p.lowChild.value = item;
                    return;
                }

                p = p.lowChild;
            }
            else if (cmp > 0)
            {
                if (p.highChild == null)
                {
                    p.highChild = new TstDictionaryEntry<T>(c);
                    p.highChild.value = item;
                    return;
                }

                p = p.highChild;
            }
            else
            {
                ++i;
                if (i == item.Key.Length)
                {
                    if (p.isKey && p.value.Key.Length == i)
                    {
                        throw new ArgumentException("key already in dictionary");
                    }
                }

                if (p.eqChild == null && p.isKey)
                {
                    p.eqChild = new TstDictionaryEntry<T>(i < p.value.Key.Length ? p.value.Key[i] : '\0');
                    p.eqChild.value = p.value;
                    p.value = default;
                }
                else if (p.eqChild == null)
                {
                    p.eqChild = new TstDictionaryEntry<T>(item.Key[i]);
                    p.eqChild.value = item;
                    return;
                }

                p = p.eqChild;
            }
        }

        p.value = item;
    }

    /// <summary>
    /// Adds multiple elements into the <see cref="TernarySearchTreeDictionary{T}"/>
    /// </summary>
    /// <param name = "values">The elements to add</param>
    /// <remarks>This method attempts to create a balanced tree</remarks>
    public void AddRange(IEnumerable<KeyValuePair<string, T>> values)
    {
        var arr = values.OrderBy(v => v.Key, this.m_comparer).ToArray();
        AddRecursive(arr, 0, arr.Length);
    }

    /// <summary>
    /// Removes all elements from the <see cref="TernarySearchTreeDictionary{T}"/>.
    /// </summary>
    public virtual void Clear()
    {
        // updating version
        ++this.m_version;
        this.m_root = null;
    }

    /// <summary>
    /// Determines whether the <see cref="TernarySearchTreeDictionary{T}"/> contains a specific key.
    /// </summary>
    /// <param name = "key">The key to locate in the <see cref="TernarySearchTreeDictionary{T}"/>.</param>
    /// <returns>true if the <see cref="TernarySearchTreeDictionary{T}"/> contains an element with the specified key; otherwise, false.</returns>
    /// <exception cref = "ArgumentNullException">
    /// <paramref name = "key"/> is a null reference (Nothing in Visual Basic).
    /// </exception>
    /// <remarks>
    /// <para>Complexity: Uses a Ternary Search Tree (tst) to find the key.</para>
    /// </remarks>
    public virtual bool ContainsKey(string key)
    {
        if (key == null)
        {
            throw new ArgumentNullException("key");
        }

        var de = Find(key);
        return de != null && de.isKey;
    }

    /// <summary>Returns an enumerator that iterates through the <see cref="TernarySearchTreeDictionary{T}"/>.</summary>
    /// <returns>A enumerator structure for the <see cref="TernarySearchTreeDictionary{T}"/>.</returns>
    public virtual IEnumerator<KeyValuePair<string, T>> GetEnumerator()
    {
        return new TstDictionaryEnumerator<T>(this);
    }

    /// <summary>
    /// Removes the exact key and prunes nodes no longer required by remaining entries.
    /// </summary>
    /// <param name="key">
    /// The nonempty key compared using the configured comparer.
    /// </param>
    /// <returns>
    /// True when an entry was removed; false when the exact key is absent.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// The key is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The key is empty.
    /// </exception>
    public virtual bool Remove(string key)
    {
        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        if (key.Length == 0)
        {
            throw new ArgumentException("key length cannot be 0");
        }

        // updating version
        ++this.m_version;
        var stack = new Stack<TstDictionaryEntry<T>>();
        if (!TryGetNode(key, stack, out TernarySearchTreeDictionary<T>.TstDictionaryEntry<T>? p))
        {
            return false;
        }

        stack.Pop();
        if (p == null)
        {
            return false;
        }

        p.value = default;
        while (!p.isKey && !p.hasChildren && stack.Count > 0)
        {
            if (stack.Peek().lowChild == p)
            {
                stack.Peek().lowChild = null;
            }
            else if (stack.Peek().highChild == p)
            {
                stack.Peek().highChild = null;
            }
            else
            {
                stack.Peek().eqChild = null;
            }

            p = stack.Pop();
        }

        if (!p.isKey && !p.hasChildren && p == this.m_root)
        {
            this.m_root = null;
        }

        return true;
    }

    /// <summary>
    /// Finds all entries matching a key prefix using the configured character comparison.
    /// </summary>
    /// <param name="key">
    /// The prefix; null or empty returns the complete dictionary.
    /// </param>
    /// <returns>
    /// Matching entries in traversal order; empty when no entry matches. A nonempty prefix produces a captured result list.
    /// </returns>
    public IEnumerable<KeyValuePair<string, T>> StartingWith(string key)
    {
        if (this.m_root == null)
        {
            return Enumerable.Empty<KeyValuePair<string, T>>();
        }

        if (string.IsNullOrEmpty(key))
        {
            return this;
        }

        var result = new List<KeyValuePair<string, T>>();
        StartingWith(this.m_root.splitChar, this.m_root.lowChild, this.m_root.eqChild, this.m_root.highChild, this.m_root.value, key, 0, result);
        return result;
    }

    private void StartingWith(
        char split,
        TstDictionaryEntry<T>? low,
        TstDictionaryEntry<T>? eq,
        TstDictionaryEntry<T>? high,
        KeyValuePair<string, T> value,
        string key,
        int index,
        IList<KeyValuePair<string, T>> matches
    ) {
        var c = index >= key.Length ? '\0' : key[index];
        var cmp = this.m_compare(c, split);
        if ((c == '\0' || cmp < 0) && low != null)
        {
            StartingWith(low.splitChar, low.lowChild, low.eqChild, low.highChild, low.value, key, index, matches);
        }

        if (c == '\0' || cmp == 0)
        {
            if (eq != null)
            {
                StartingWith(eq.splitChar, eq.lowChild, eq.eqChild, eq.highChild, eq.value, key, index + 1, matches);
            }
            else if (value.Key != null && index < value.Key.Length - 1)
            {
                StartingWith(value.Key[index + 1], null, null, null, value, key, index + 1, matches);
            }
            else if (value.Key != null && value.Key.Length >= key.Length)
            {
                matches.Add(value);
            }
        }

        if ((c == '\0' || cmp > 0) && high != null)
        {
            StartingWith(high.splitChar, high.lowChild, high.eqChild, high.highChild, high.value, key, index, matches);
        }
    }

    /// <summary>Gets the _value associated with the specified key.</summary>
    /// <returns><c>true</c> if the <see cref="T:System.Collections.Generic.Dictionary`2"/> contains an element with the specified key; otherwise, <c>false</c>.</returns>
    /// <param name = "key">The key of the _value to get.</param>
    /// <param name = "value">When this method returns, contains the _value associated with the specified key, if the key is found; otherwise, the default _value for the type of the <paramref name = "value"/> parameter. This parameter is passed uninitialized.</param>
    /// <exception cref="T:System.ArgumentNullException">
    /// <paramref name = "key"/> is null.
    /// </exception>
    public bool TryGetValue(
        string key,
        out T value
    ) {
#pragma warning disable CS8601 // Possible null reference assignment.

        value = default;
#pragma warning restore CS8601 // Possible null reference assignment.

        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        if (!TryGetNode(key, null, out TernarySearchTreeDictionary<T>.TstDictionaryEntry<T>? entry))
        {
            return false;
        }

        if (entry == null)
        {
            return false;
        }
        else
        {
            value = entry.value.Value;
        }

        return true;
    }

    /// <summary>
    /// Finds the retained tree node for an exact key.
    /// </summary>
    /// <param name="key">
    /// The nonempty key compared using the configured comparer.
    /// </param>
    /// <returns>
    /// The retained node for the exact key, or null when absent.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// The key is null or empty.
    /// </exception>
    protected virtual TstDictionaryEntry<T>? Find(string key)
    {
        if (TryGetNode(key, null, out TernarySearchTreeDictionary<T>.TstDictionaryEntry<T>? result))
        {
            return result;
        }

        return null;
    }

    /// <summary>
    /// Finds an exact key and optionally records the visited nodes for pruning.
    /// </summary>
    /// <param name="key">
    /// The nonempty key compared using the configured comparer.
    /// </param>
    /// <param name="stack">
    /// The destination traversal stack, or null when the path is not needed; existing entries are retained.
    /// </param>
    /// <param name="entry">
    /// The retained exact-key node on success, or null when absent.
    /// </param>
    /// <returns>
    /// True when an exact key exists; otherwise false.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// The key is null or empty.
    /// </exception>
    protected virtual bool TryGetNode(
        string key,
        Stack<TstDictionaryEntry<T>>? stack,
        out TstDictionaryEntry<T>? entry
    ) {
        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        var n = key.Length;
        if (n == 0)
        {
            throw new ArgumentNullException(nameof(key));
        }

        var p = this.m_root;
        var index = 0;
        int cmp;
        while (index < n && p != null)
        {
            if (stack != null)
            {
                stack.Push(p);
            }

            cmp = this.m_compare(key[index], p.splitChar);
            if (cmp < 0)
            {
                p = p.lowChild;
            }
            else if (cmp > 0)
            {
                p = p.highChild;
            }
            else
            {
                if (p.value.Key != null)
                {
                    bool res = this.m_comparer.Compare(p.value.Key, key) == 0;
                    entry = res ? p : null;
                    return res;
                }
                else
                {
                    ++index;
                    p = p.eqChild;
                }
            }
        }

        bool found = p is not null && p.isKey && this.m_comparer.Compare(p.value.Key, key) == 0;
        entry = found ? p : null;
        return found;
    }

    private void AddRecursive(
        KeyValuePair<string, T>[] values,
        int start,
        int count
    ) {
        switch (count)
        {
            case 0:
                break;
            case 1:
                Add(values[start]);
                break;
            case 2:
                Add(values[start]);
                Add(values[start + 1]);
                break;
            default:
                var bucket = count / 2 - (count + 1) % 2;
                for (var i = start + bucket; i < start + count - bucket; i++)
                {
                    Add(values[i]);
                }

                AddRecursive(values, start, bucket);
                AddRecursive(values, start + count - bucket, bucket);
                break;
        }
    }

    #region Explicit Interfaces
    /// <summary>
    /// Returns an <see cref = "IDictionaryEnumerator"/> that can iterate through the <see cref="TernarySearchTreeDictionary{T}"/>.
    /// </summary>
    /// <returns>An <see cref = "IDictionaryEnumerator"/> for the <see cref="TernarySearchTreeDictionary{T}"/>.</returns>
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    IEnumerable<string> IReadOnlyDictionary<string, T>.Keys
    {
        get
        {
            return Keys;
        }
    }

    IEnumerable<T> IReadOnlyDictionary<string, T>.Values
    {
        get
        {
            return Values;
        }
    }

    /// <summary>
    /// Get a _value indicating whether access to the <see cref="TernarySearchTreeDictionary{T}"/> is synchronized (thread-safe).
    /// </summary>
    /// <value>
    /// true if access to the <see cref="TernarySearchTreeDictionary{T}"/> is synchronized (thread-safe);
    /// otherwise, false. The default is false.
    /// </value>
    bool ICollection.IsSynchronized
    {
        get
        {
            return false;
        }
    }

    /// <summary>
    /// Gets an object that can be used to synchronize access to the <see cref="TernarySearchTreeDictionary{T}"/>.
    /// </summary>
    /// <value>
    /// An object that can be used to synchronize access to the <see cref="TernarySearchTreeDictionary{T}"/>.
    /// </value>
    object ICollection.SyncRoot
    {
        get
        {
            return this;
        }
    }

    /// <summary>
    /// Copies the <see cref="TernarySearchTreeDictionary{T}"/> elements to a one-dimensional Array instance at the specified index.
    /// </summary>
    /// <param name = "array">The one-dimensional <see cref = "Array"/> that is the destination of the
    /// <see cref = "DictionaryEntry"/>
    /// objects copied from <see cref="TernarySearchTreeDictionary{T}"/>. The <see cref = "Array"/> must have zero-based indexing.
    /// </param>
    /// <param name = "arrayIndex">The zero-based index in <paramref name = "array"/> at which copying begins.</param>
    /// <exception cref = "ArgumentNullException"><paramref name = "array"/> is a null reference</exception>
    /// <exception cref = "ArgumentOutOfRangeException">
    /// <paramref name = "arrayIndex"/> is less than zero.
    /// </exception>
    /// <exception cref = "ArgumentException">
    /// <paramref name = "array"/> is multidimensional.
    /// </exception>
    /// <exception cref = "ArgumentException">
    /// <paramref name = "arrayIndex"/> is equal to or greater than the length of <paramref name = "array"/>.
    /// </exception>
    /// <exception cref = "ArgumentException">
    /// The number of elements in the source <see cref="TernarySearchTreeDictionary{T}"/> is greater than
    /// the available space from <paramref name = "arrayIndex"/> to the end of the destination array.
    /// </exception>
    /// <exception cref = "ArgumentException">
    /// The type of the source <see cref="TernarySearchTreeDictionary{T}"/> cannot be cast automatically
    /// to the type of the destination array.
    /// </exception>
    void ICollection.CopyTo(
        Array array,
        int arrayIndex
    ) {
        if (array == null)
        {
            throw new ArgumentNullException("array");
        }

        if (arrayIndex < 0)
        {
            throw new ArgumentOutOfRangeException("index is negative");
        }

        if (array.Rank > 1)
        {
            throw new ArgumentException("array is multi-dimensional");
        }

        if (array.GetLowerBound(0) != 0)
        {
            throw new ArgumentException("The destination array must have zero-based indexing.", nameof(array));
        }

        ValidateCopyCapacity(array.Length, arrayIndex);
        var i = arrayIndex;
        foreach (var de in this)
        {
            array.SetValue(de, i++);
        }
    }

    bool ICollection<KeyValuePair<string, T>>.IsReadOnly
    {
        get
        {
            return false;
        }
    }

    bool ICollection<KeyValuePair<string, T>>.Contains(KeyValuePair<string, T> item)
    {
        return TryGetValue(item.Key, out T? value) && Equals(item.Value, value);
    }

    void ICollection<KeyValuePair<string, T>>.CopyTo(
        KeyValuePair<string, T>[] array,
        int arrayIndex
    ) {
        if (array == null)
        {
            throw new ArgumentNullException("array");
        }

        if (arrayIndex < 0)
        {
            throw new ArgumentOutOfRangeException("index is negative");
        }

        if (array.Rank > 1)
        {
            throw new ArgumentException("array is multi-dimensional");
        }

        ValidateCopyCapacity(array.Length, arrayIndex);
        var i = arrayIndex;
        foreach (var de in this)
        {
            array[i++] = de;
        }
    }

    bool ICollection<KeyValuePair<string, T>>.Remove(KeyValuePair<string, T> item)
    {
        return TryGetValue(item.Key, out T? value) && EqualityComparer<T>.Default.Equals(value, item.Value) && Remove(item.Key);
    }

    private void ValidateCopyCapacity(
        int arrayLength,
        int arrayIndex
    ) {
        if ((uint)arrayIndex > (uint)arrayLength)
            throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        if (Count > arrayLength - arrayIndex)
            throw new ArgumentException("The destination array does not have enough remaining capacity.");
    }

    #endregion Explicit Interfaces
    /// <summary>
    /// Enumerates stored entries and rejects access after the owning dictionary changes.
    /// </summary>
    /// <typeparam name="S">
    /// The value type stored by the owning dictionary.
    /// </typeparam>
    protected sealed class TstDictionaryEnumerator<S> : IEnumerator<KeyValuePair<string, S>>
    {
        private TernarySearchTreeDictionary<S>.TstDictionaryEntry<S>? m_currentNode;
        private readonly TernarySearchTreeDictionary<S>? m_dictionary;
        private Stack<TernarySearchTreeDictionary<S>.TstDictionaryEntry<S>>? m_stack;
        private readonly long m_version;
        /// <summary>Constructs an enumerator over <paramref name = "tst"/></summary>
        /// <param name = "tst">dictionary to enumerate.</param>
        /// <exception cref = "ArgumentNullException">tst is null</exception>
        public TstDictionaryEnumerator(TernarySearchTreeDictionary<S> tst)
        {
            this.m_currentNode = null;
            this.m_dictionary = tst ?? throw new ArgumentNullException(nameof(tst));
            this.m_stack = null;
            this.m_version = tst.m_version;
        }

        /// <summary>
        /// Sets the enumerator to its initial position, which is before the first element in the collection.
        /// </summary>
        public void Reset()
        {
            ThrowIfChanged();
            this.m_stack?.Clear();
            this.m_stack = null;
            this.m_currentNode = null;
        }

        /// <summary>
        /// Gets the current element in the collection.
        /// </summary>
        /// <value>The current element in the collection.</value>
        public KeyValuePair<string, S> Current
        {
            get
            {
                ThrowIfChanged();
                if (this.m_currentNode == null)
                {
                    throw new InvalidOperationException();
                }

                return this.m_currentNode.value;
            }
        }

        /// <summary>
        /// Gets the current element in the collection.
        /// </summary>
        /// <value>The current element in the collection.</value>
        object IEnumerator.Current
        {
            get
            {
                return Current;
            }
        }

        /// <summary>
        /// Advances the enumerator to the next element of the collection.
        /// </summary>
        /// <returns>
        /// true if the enumerator was successfully advanced to the next element;
        /// false if the enumerator has passed the end of the collection.
        /// </returns>
        public bool MoveNext()
        {
            ThrowIfChanged();
            // we are at the beginning
            if (this.m_stack == null)
            {
                this.m_stack = new Stack<TernarySearchTreeDictionary<S>.TstDictionaryEntry<S>>();
                this.m_currentNode = null;
                if (this.m_dictionary?.m_root != null)
                {
                    this.m_stack.Push(this.m_dictionary.m_root);
                }
            }
            // we are at the end node, finished
            else if (this.m_currentNode == null)
                return false;

            if (this.m_stack.Count == 0)
            {
                this.m_currentNode = null;
            }

            while (this.m_stack.Count > 0)
            {
                this.m_currentNode = this.m_stack.Pop();
                if (this.m_currentNode.highChild != null)
                {
                    this.m_stack.Push(this.m_currentNode.highChild);
                }

                if (this.m_currentNode.eqChild != null)
                {
                    this.m_stack.Push(this.m_currentNode.eqChild);
                }

                if (this.m_currentNode.lowChild != null)
                {
                    this.m_stack.Push(this.m_currentNode.lowChild);
                }

                if (this.m_currentNode.isKey)
                {
                    break;
                }
            }

            return this.m_currentNode != null;
        }

        internal void ThrowIfChanged()
        {
            if (this.m_version != this.m_dictionary?.m_version)
            {
                throw new InvalidOperationException("Collection changed");
            }
        }

        /// <summary>
        /// Completes the enumeration without releasing external resources; dictionary ownership remains with the caller.
        /// </summary>
        public void Dispose()
        {
            // Do nothing
        }
    }

    /// <summary>
    /// Retains one tree split character, optional compressed key, and child links used by dictionary implementations.
    /// </summary>
    /// <typeparam name="S">
    /// The value type retained in an optional key-value pair.
    /// </typeparam>
    protected class TstDictionaryEntry<S>
    {
        private TstDictionaryEntry<S>? m_eqChild;
        private TstDictionaryEntry<S>? m_highChild;
        private TstDictionaryEntry<S>? m_lowChild;
        private readonly char m_splitChar;
        private KeyValuePair<string, S> m_value;
        /// <summary>
        /// Construct a tst node.
        /// </summary>
        /// <param name = "splitChar">split character</param>
        public TstDictionaryEntry(char splitChar)
        {
            this.m_splitChar = splitChar;
            this.m_lowChild = null;
            this.m_eqChild = null;
            this.m_highChild = null;
        }

        /// <summary>
        /// Gets the split character.
        /// </summary>
        /// <value>
        /// The split character.
        /// </value>
        public char splitChar
        {
            get
            {
                return this.m_splitChar;
            }
        }

        /// <summary>
        /// Gets a _value indicating wheter the node is a key.
        /// </summary>
        /// <value>
        /// true is the node is a key, false otherwize.
        /// </value>
        public bool isKey
        {
            get
            {
                return this.m_value.Key != null;
            }
        }

        /// <summary>
        /// Gets or replaces the stored key-value pair; a default pair marks the node as a structural node without a key.
        /// </summary>
        public KeyValuePair<string, S> value
        {
            get
            {
                return this.m_value;
            }

            set
            {
                this.m_value = value;
            }
        }

        /// <summary>
        /// Gets the node low child.
        /// </summary>
        /// <value>
        /// The low child.
        /// </value>
        public TstDictionaryEntry<S>? lowChild
        {
            get
            {
                return this.m_lowChild;
            }

            set
            {
                this.m_lowChild = value;
            }
        }

        /// <summary>
        /// Gets the node ep child.
        /// </summary>
        /// <value>
        /// The eq child.
        /// </value>
        public TstDictionaryEntry<S>? eqChild
        {
            get
            {
                return this.m_eqChild;
            }

            set
            {
                this.m_eqChild = value;
            }
        }

        /// <summary>
        /// Gets the node high child.
        /// </summary>
        /// <value>
        /// The high child.
        /// </value>
        public TstDictionaryEntry<S>? highChild
        {
            get
            {
                return this.m_highChild;
            }

            set
            {
                this.m_highChild = value;
            }
        }

        /// <summary>
        /// Gets a _value indicating wheter the node has children.
        /// </summary>
        /// <value>
        /// true if the node has children, false otherwize.
        /// </value>
        public bool hasChildren
        {
            get
            {
                return this.lowChild != null || this.eqChild != null || this.highChild != null;
            }
        }

        /// <summary>
        /// Formats the split character and any retained key for tree diagnostics.
        /// </summary>
        /// <returns>
        /// The split character followed by the stored key when this is a key-bearing node.
        /// </returns>
        public override string ToString()
        {
            if (this.isKey)
            {
                return string.Format("{0} {1}", this.splitChar, this.m_value.Key);
            }
            else
            {
                return string.Format("{0}", this.splitChar);
            }
        }
    }
}
