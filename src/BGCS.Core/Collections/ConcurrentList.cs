using System;
using System.Collections;
using System.Collections.Generic;

namespace BGCS.Core.Collections;

/// <summary>
/// Serializes individual list operations and enumerates an owned snapshot captured under the same lock.
/// </summary>
/// <typeparam name="T">The type of elements retained by the list.</typeparam>
/// <remarks>
/// Operations do not make a caller's multi-step workflow atomic. Lock syncObject around such workflows.
/// Enumerators retain their snapshot and never hold the list lock while consumer code runs.
/// </remarks>
public class ConcurrentList<T> : IList<T>, IReadOnlyList<T>
{
    private readonly List<T> m_list;
    private readonly object m_lock = new();

    /// <summary>Creates an empty list with the default initial capacity.</summary>
    public ConcurrentList() => m_list = new();

    /// <summary>Creates an empty list with space reserved for the requested number of elements.</summary>
    /// <param name="capacity">The non-negative initial capacity.</param>
    /// <exception cref="ArgumentOutOfRangeException">The capacity is negative.</exception>
    public ConcurrentList(int capacity) => m_list = new(capacity);

    /// <summary>Copies an initial sequence into a new, independently owned list.</summary>
    /// <param name="values">The sequence to enumerate once; its elements are retained by reference when applicable.</param>
    /// <exception cref="ArgumentNullException">The sequence is null.</exception>
    public ConcurrentList(IEnumerable<T> values) => m_list = new(values);

    /// <inheritdoc />
    public T this[int index]
    {
        get
        {
            lock (m_lock)
                return m_list[index];
        }
        set
        {
            lock (m_lock)
                m_list[index] = value;
        }
    }

    /// <inheritdoc />
    public int Count
    {
        get
        {
            lock (m_lock)
                return m_list.Count;
        }
    }

    /// <inheritdoc />
    public bool IsReadOnly => false;

    /// <summary>Gets the monitor shared by all operations for caller-owned atomic compound workflows.</summary>
    public object syncObject => m_lock;

    /// <inheritdoc />
    public void Add(T item)
    {
        lock (m_lock)
            m_list.Add(item);
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (m_lock)
            m_list.Clear();
    }

    /// <inheritdoc />
    public bool Contains(T item)
    {
        lock (m_lock)
            return m_list.Contains(item);
    }

    /// <inheritdoc />
    public void CopyTo(
        T[] array,
        int arrayIndex
    ) {
        lock (m_lock)
            m_list.CopyTo(array, arrayIndex);
    }

    /// <inheritdoc />
    /// <remarks>Subsequent mutations cannot invalidate or alter the captured sequence.</remarks>
    public IEnumerator<T> GetEnumerator()
    {
        lock (m_lock)
            return ((IEnumerable<T>)m_list.ToArray()).GetEnumerator();
    }

    /// <inheritdoc />
    public int IndexOf(T item)
    {
        lock (m_lock)
            return m_list.IndexOf(item);
    }

    /// <inheritdoc />
    public void Insert(
        int index,
        T item
    ) {
        lock (m_lock)
            m_list.Insert(index, item);
    }

    /// <inheritdoc />
    public bool Remove(T item)
    {
        lock (m_lock)
            return m_list.Remove(item);
    }

    /// <inheritdoc />
    public void RemoveAt(int index)
    {
        lock (m_lock)
            m_list.RemoveAt(index);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
