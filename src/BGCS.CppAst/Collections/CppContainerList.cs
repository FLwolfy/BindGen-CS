using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Interfaces;

namespace BGCS.CppAst.Collections;

/// <summary>
/// Owns ordered AST children and maintains each child's unique parent association during list mutation.
/// </summary>
/// <typeparam name="TElement">
/// The AST node type owned by this collection.
/// </typeparam>
[DebuggerTypeProxy(typeof(CppContainerListDebugView<>))]
[DebuggerDisplay("Count = {Count}")]
public class CppContainerList<TElement> : ICollection<TElement>, IEnumerable<TElement>, IEnumerable, IList<TElement>, IReadOnlyCollection<TElement>, IReadOnlyList<TElement> where TElement : CppElement
{
    private readonly List<TElement> m_elements;
    /// <summary>
    /// Creates an empty child collection attached to one non-null AST container.
    /// </summary>
    /// <param name="container">
    /// The parent retained for every child attached to this list.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// The parent container is null.
    /// </exception>
    public CppContainerList(ICppContainer container)
    {
        this.container = container ?? throw new ArgumentNullException(nameof(container));
        this.m_elements = [];
    }

    /// <summary>
    /// Gets the container this list is attached to.
    /// </summary>
    public ICppContainer container { get; }

    /// <inheritdoc/>
    public IEnumerator<TElement> GetEnumerator()
    {
        return this.m_elements.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable)this.m_elements).GetEnumerator();
    }

    /// <inheritdoc/>
    public void Add(TElement item)
    {
        ValidateOwnership(item);
        this.m_elements.Add(item);
        item.parent = this.container;
    }

    /// <summary>
    /// Attaches children in enumeration order; a later invalid child does not undo earlier attachments.
    /// </summary>
    /// <param name="collection">
    /// Detached children to attach, or null to leave the collection unchanged.
    /// </param>
    /// <exception cref="ArgumentException">
    /// An enumerated child already has a parent or would introduce an ownership cycle.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// An enumerated child is null.
    /// </exception>
    public void AddRange(IEnumerable<TElement> collection)
    {
        if (collection != null)
        {
            foreach (var element in collection)
            {
                Add(element);
            }
        }
    }

    /// <inheritdoc/>
    public void Clear()
    {
        foreach (var element in this.m_elements)
        {
            element.parent = null;
        }

        this.m_elements.Clear();
    }

    /// <inheritdoc/>
    public bool Contains(TElement item)
    {
        return this.m_elements.Contains(item);
    }

    /// <inheritdoc/>
    public void CopyTo(
        TElement[] array,
        int arrayIndex
    ) {
        this.m_elements.CopyTo(array, arrayIndex);
    }

    /// <inheritdoc/>
    public bool Remove(TElement item)
    {
        if (this.m_elements.Remove(item))
        {
            item.parent = null;
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public int Count => this.m_elements.Count;
    /// <inheritdoc/>
    public bool IsReadOnly => false;

    /// <inheritdoc/>
    public int IndexOf(TElement item)
    {
        return this.m_elements.IndexOf(item);
    }

    /// <inheritdoc/>
    public void Insert(
        int index,
        TElement item
    ) {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, this.m_elements.Count);
        ValidateOwnership(item);
        this.m_elements.Insert(index, item);
        item.parent = this.container;
    }

    /// <inheritdoc/>
    public void RemoveAt(int index)
    {
        var element = this.m_elements[index];
        element.parent = null;
        this.m_elements.RemoveAt(index);
    }

    /// <inheritdoc/>
    public TElement this[int index]
    {
        get => this.m_elements[index];
        set
        {
            TElement previous = this.m_elements[index];
            if (ReferenceEquals(previous, value))
            {
                return;
            }
            ValidateOwnership(value);
            this.m_elements[index] = value;
            previous.parent = null;
            value.parent = this.container;
        }
    }

    /// <summary>
    /// Finds the first ordered child accepted by a caller-supplied predicate without capturing its context.
    /// </summary>
    /// <typeparam name="TUserdata">
    /// The lookup context type, including ref-struct contexts.
    /// </typeparam>
    /// <param name="userdata">
    /// The caller's lookup context, passed unchanged to each predicate invocation.
    /// </param>
    /// <param name="selector">
    /// The predicate applied to each current child until the first match.
    /// </param>
    /// <returns>
    /// The borrowed matching child, or null when the predicate accepts no child.
    /// </returns>
    public TElement? Find<TUserdata>(
        TUserdata userdata,
        Func<TElement, TUserdata, bool> selector
    )
        where TUserdata : allows ref struct
    {
        foreach (var element in this.m_elements)
        {
            if (selector(element, userdata))
            {
                return element;
            }
        }

        return null;
    }

    private void ValidateOwnership(TElement item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.parent is not null)
        {
            throw new ArgumentException("The item already belongs to a container.", nameof(item));
        }
        for (CppElement? ancestor = this.container as CppElement; ancestor is not null; ancestor = ancestor.parent as CppElement)
        {
            if (ReferenceEquals(ancestor, item))
            {
                throw new ArgumentException("A container cannot own itself or an ancestor.", nameof(item));
            }
        }
    }
}

/// <summary>
/// Provides ordinal name lookup over container-owned AST child collections.
/// </summary>
public static class CppContainerListExtensions
{
    /// <summary>
    /// Returns the first child whose current member name exactly matches the supplied character span.
    /// </summary>
    /// <typeparam name="TElement">
    /// The AST child type that exposes a native member name.
    /// </typeparam>
    /// <param name="list">
    /// The child collection borrowed for the lookup.
    /// </param>
    /// <param name="name">
    /// The case-sensitive native member name to find.
    /// </param>
    /// <returns>
    /// The borrowed matching child, or null when no member has that name.
    /// </returns>
    public static TElement? FindElementByName<TElement>(
        this CppContainerList<TElement> list,
        ReadOnlySpan<char> name
    )
        where TElement : CppElement, ICppMember
    {
        return list.Find(name, static (
            x,
            name
        ) => name.SequenceEqual(x.name));
    }
}

internal class CppContainerListDebugView<T>
{
    private readonly ICollection<T> m_collection;
    public CppContainerListDebugView(ICollection<T> collection)
    {
        this.m_collection = collection ?? throw new ArgumentNullException(nameof(collection));
    }

    [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
    public T[] items
    {
        get
        {
            T[] array = new T[this.m_collection.Count];
            this.m_collection.CopyTo(array, 0);
            return array;
        }
    }
}
