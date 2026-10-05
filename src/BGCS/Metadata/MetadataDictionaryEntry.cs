using System;
using System.Collections.Generic;

namespace BGCS.Metadata
{
    using System.Collections;
    using System.Diagnostics.CodeAnalysis;
    using BGCS.Core.Collections;

    /// <summary>
    /// Stores mutable keyed generation metadata in an owned dictionary while retaining key and value references.
    /// </summary>
    /// <typeparam name="TKey">The non-null metadata key type.</typeparam>
    /// <typeparam name="TValue">The retained metadata value type.</typeparam>
    public class MetadataDictionaryEntry<TKey, TValue> : GeneratorMetadataEntry, IDictionary<TKey, TValue> where TKey : notnull
    {
        private readonly Dictionary<TKey, TValue> m_dictionary = [];
        /// <summary>
        /// Creates an empty mutable keyed metadata collection.
        /// </summary>
        public MetadataDictionaryEntry()
        {
        }

        /// <summary>
        /// Copies the source dictionary container and preserves its key comparer.
        /// </summary>
        /// <param name="other">
        /// The dictionary to copy; its key and value objects remain shared.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// The source dictionary is null.
        /// </exception>
        public MetadataDictionaryEntry(Dictionary<TKey, TValue> other)
        {
            ArgumentNullException.ThrowIfNull(other);
            m_dictionary = new(other, other.Comparer);
        }

        /// <inheritdoc />
        public TValue this[TKey key] { get => ((IDictionary<TKey, TValue>)this.m_dictionary)[key]; set => ((IDictionary<TKey, TValue>)this.m_dictionary)[key] = value; }

        /// <inheritdoc />
        public ICollection<TKey> Keys => ((IDictionary<TKey, TValue>)this.m_dictionary).Keys;
        /// <inheritdoc />
        public ICollection<TValue> Values => ((IDictionary<TKey, TValue>)this.m_dictionary).Values;
        /// <summary>
        /// Gets the owned mutable dictionary used by generator extensions; changes immediately affect this entry.
        /// </summary>
        public Dictionary<TKey, TValue> dictionary => this.m_dictionary;
        /// <inheritdoc />
        public int Count => ((ICollection<KeyValuePair<TKey, TValue>>)this.m_dictionary).Count;
        /// <inheritdoc />
        public bool IsReadOnly => ((ICollection<KeyValuePair<TKey, TValue>>)this.m_dictionary).IsReadOnly;

        /// <inheritdoc />
        public void Add(
            TKey key,
            TValue value
        ) {
            ((IDictionary<TKey, TValue>)this.m_dictionary).Add(key, value);
        }

        /// <inheritdoc />
        public void Add(KeyValuePair<TKey, TValue> item)
        {
            ((ICollection<KeyValuePair<TKey, TValue>>)this.m_dictionary).Add(item);
        }

        /// <inheritdoc />
        public void Clear()
        {
            ((ICollection<KeyValuePair<TKey, TValue>>)this.m_dictionary).Clear();
        }

        /// <summary>
        /// Copies the dictionary container and comparer without recursively cloning its key or value objects.
        /// </summary>
        /// <returns>
        /// A new keyed metadata entry whose keys and values remain shared with this entry.
        /// </returns>
        public override GeneratorMetadataEntry Clone()
        {
            return new MetadataDictionaryEntry<TKey, TValue>(this.m_dictionary);
        }

        /// <inheritdoc />
        public bool Contains(KeyValuePair<TKey, TValue> item)
        {
            return ((ICollection<KeyValuePair<TKey, TValue>>)this.m_dictionary).Contains(item);
        }

        /// <inheritdoc />
        public bool ContainsKey(TKey key)
        {
            return ((IDictionary<TKey, TValue>)this.m_dictionary).ContainsKey(key);
        }

        /// <inheritdoc />
        public void CopyTo(
            KeyValuePair<TKey, TValue>[] array,
            int arrayIndex
        ) {
            ((ICollection<KeyValuePair<TKey, TValue>>)this.m_dictionary).CopyTo(array, arrayIndex);
        }

        /// <summary>
        /// Copies entries into an existing dictionary, replacing values for keys recognized by its comparer.
        /// </summary>
        /// <param name="other">
        /// The destination dictionary; existing unrelated entries are retained.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// The destination is null.
        /// </exception>
        public void CopyTo(Dictionary<TKey, TValue> other)
        {
            other.AddRange(this.m_dictionary);
        }

        /// <summary>
        /// Copies source entries into this dictionary, replacing values for keys recognized by its comparer.
        /// </summary>
        /// <param name="other">
        /// The source dictionary; its container remains unchanged.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// The source is null.
        /// </exception>
        public void CopyFrom(Dictionary<TKey, TValue> other)
        {
            this.m_dictionary.AddRange(other);
        }

        /// <inheritdoc />
        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            return ((IEnumerable<KeyValuePair<TKey, TValue>>)this.m_dictionary).GetEnumerator();
        }

        /// <summary>
        /// Overwrites matching keys and adds new keys from compatible dictionary metadata; incompatible entry types are ignored.
        /// </summary>
        /// <param name="from">
        /// The candidate source metadata.
        /// </param>
        /// <param name="options">
        /// Merge policy; dictionary entries do not use optional function-table settings.
        /// </param>
        public override void Merge(
            GeneratorMetadataEntry from,
            in MergeOptions options
        ) {
            if (from is MetadataDictionaryEntry<TKey, TValue> dict)
            {
                this.m_dictionary.AddRange(dict.m_dictionary);
            }
        }

        /// <inheritdoc />
        public bool Remove(TKey key)
        {
            return ((IDictionary<TKey, TValue>)this.m_dictionary).Remove(key);
        }

        /// <inheritdoc />
        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            return ((ICollection<KeyValuePair<TKey, TValue>>)this.m_dictionary).Remove(item);
        }

        /// <inheritdoc />
        public bool TryGetValue(
            TKey key,
            [MaybeNullWhen(false)] out TValue value
        ) {
            return ((IDictionary<TKey, TValue>)this.m_dictionary).TryGetValue(key, out value);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)this.m_dictionary).GetEnumerator();
        }
    }
}
