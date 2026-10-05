using System;
using System.Collections.Generic;
using System.Linq;

namespace BGCS.Metadata
{
    using System.Collections;
    using BGCS.Core.Collections;

    /// <summary>
    /// Stores mutable generation metadata in an owned ordered list.
    /// </summary>
    /// <typeparam name="T">The retained metadata element type.</typeparam>
    public class MetadataListEntry<T> : GeneratorMetadataEntry, IList<T>
    {
        private readonly List<T> m_values = [];
        /// <summary>
        /// Creates an empty mutable metadata sequence.
        /// </summary>
        public MetadataListEntry()
        {
        }

        /// <summary>
        /// Copies the source sequence into an owned container while retaining its elements.
        /// </summary>
        /// <param name="values">
        /// The source sequence in enumeration order.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// The source sequence is null.
        /// </exception>
        public MetadataListEntry(IEnumerable<T> values)
        {
            this.m_values.AddRange(values);
        }

        /// <inheritdoc />
        public T this[int index] { get => ((IList<T>)this.m_values)[index]; set => ((IList<T>)this.m_values)[index] = value; }

        /// <inheritdoc />
        public int Count => ((ICollection<T>)this.m_values).Count;
        /// <inheritdoc />
        public bool IsReadOnly => ((ICollection<T>)this.m_values).IsReadOnly;
        /// <summary>
        /// Gets the owned mutable sequence used by generator extensions; changes immediately affect this entry.
        /// </summary>
        public List<T> values => this.m_values;

        /// <inheritdoc />
        public void Add(T item)
        {
            ((ICollection<T>)this.m_values).Add(item);
        }

        /// <inheritdoc />
        public void Clear()
        {
            ((ICollection<T>)this.m_values).Clear();
        }

        /// <summary>
        /// Copies the list container and clones elements implementing the typed cloning contract; other element references and nulls are retained.
        /// </summary>
        /// <returns>
        /// An owned metadata sequence following the element type's cloning policy.
        /// </returns>
        public override GeneratorMetadataEntry Clone()
        {
            if (typeof(T).IsAssignableTo(typeof(ICloneable<T>)))
            {
                return new MetadataListEntry<T>(this.m_values.Select(static value => value is ICloneable<T> cloneable ? cloneable.Clone() : value));
            }

            return new MetadataListEntry<T>(this.m_values);
        }

        /// <inheritdoc />
        public bool Contains(T item)
        {
            return ((ICollection<T>)this.m_values).Contains(item);
        }

        /// <inheritdoc />
        public void CopyTo(
            T[] array,
            int arrayIndex
        ) {
            ((ICollection<T>)this.m_values).CopyTo(array, arrayIndex);
        }

        /// <summary>
        /// Appends this entry's elements to an existing list in their current order.
        /// </summary>
        /// <param name="other">
        /// The destination list; its existing elements are retained.
        /// </param>
        public void CopyTo(List<T> other)
        {
            other.AddRange(this.m_values);
        }

        /// <summary>
        /// Adds this entry's elements to a destination set using that set's comparer.
        /// </summary>
        /// <param name="other">
        /// The destination set; duplicates are ignored.
        /// </param>
        public void CopyTo(HashSet<T> other)
        {
            other.AddRange(this.m_values);
        }

        /// <inheritdoc />
        public IEnumerator<T> GetEnumerator()
        {
            return ((IEnumerable<T>)this.m_values).GetEnumerator();
        }

        /// <inheritdoc />
        public int IndexOf(T item)
        {
            return ((IList<T>)this.m_values).IndexOf(item);
        }

        /// <inheritdoc />
        public void Insert(
            int index,
            T item
        ) {
            ((IList<T>)this.m_values).Insert(index, item);
        }

        /// <summary>
        /// Appends compatible list metadata in source order; incompatible entry types are ignored.
        /// </summary>
        /// <param name="from">
        /// The candidate source metadata.
        /// </param>
        /// <param name="options">
        /// Merge policy; list entries do not use optional function-table settings.
        /// </param>
        public override void Merge(
            GeneratorMetadataEntry from,
            in MergeOptions options
        ) {
            if (from is MetadataListEntry<T> list)
            {
                this.m_values.AddRange(list);
            }
        }

        /// <inheritdoc />
        public bool Remove(T item)
        {
            return ((ICollection<T>)this.m_values).Remove(item);
        }

        /// <inheritdoc />
        public void RemoveAt(int index)
        {
            ((IList<T>)this.m_values).RemoveAt(index);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)this.m_values).GetEnumerator();
        }
    }
}
