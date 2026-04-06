namespace BGCS.Metadata
{
    using BGCS.Core.Collections;
    using System.Collections;
    using System.Diagnostics.CodeAnalysis;

    /// <summary>
    /// Defines the public class <c>MetadataDictionaryEntry</c>.
    /// </summary>
    public class MetadataDictionaryEntry<TKey, TValue> : GeneratorMetadataEntry, IDictionary<TKey, TValue> where TKey : notnull
    {
        private readonly Dictionary<TKey, TValue> dictionary = [];

        /// <summary>
        /// Initializes a new instance of <see cref="MetadataDictionaryEntry"/>.
        /// </summary>
        public MetadataDictionaryEntry()
        {
        }

        /// <summary>
        /// Executes public operation <c>MetadataDictionaryEntry</c>.
        /// </summary>
        public MetadataDictionaryEntry(Dictionary<TKey, TValue> other)
        {
            dictionary.AddRange(other);
        }

        /// <summary>
        /// Executes public operation <c>Member</c>.
        /// </summary>
        public TValue this[TKey key] { get => ((IDictionary<TKey, TValue>)dictionary)[key]; set => ((IDictionary<TKey, TValue>)dictionary)[key] = value; }

        /// <summary>
        /// Executes public operation <c>ICollection</c>.
        /// </summary>
        public ICollection<TKey> Keys => ((IDictionary<TKey, TValue>)dictionary).Keys;

        /// <summary>
        /// Executes public operation <c>ICollection</c>.
        /// </summary>
        public ICollection<TValue> Values => ((IDictionary<TKey, TValue>)dictionary).Values;

        /// <summary>
        /// Exposes public member <c>dictionary</c>.
        /// </summary>
        public Dictionary<TKey, TValue> Dictionary => dictionary;

        /// <summary>
        /// Executes public operation <c>Member</c>.
        /// </summary>
        public int Count => ((ICollection<KeyValuePair<TKey, TValue>>)dictionary).Count;

        /// <summary>
        /// Executes public operation <c>Member</c>.
        /// </summary>
        public bool IsReadOnly => ((ICollection<KeyValuePair<TKey, TValue>>)dictionary).IsReadOnly;

        /// <summary>
        /// Adds data or behavior through <c>Add</c>.
        /// </summary>
        public void Add(TKey key, TValue value)
        {
            ((IDictionary<TKey, TValue>)dictionary).Add(key, value);
        }

        /// <summary>
        /// Adds data or behavior through <c>Add</c>.
        /// </summary>
        public void Add(KeyValuePair<TKey, TValue> item)
        {
            ((ICollection<KeyValuePair<TKey, TValue>>)dictionary).Add(item);
        }

        /// <summary>
        /// Executes public operation <c>Clear</c>.
        /// </summary>
        public void Clear()
        {
            ((ICollection<KeyValuePair<TKey, TValue>>)dictionary).Clear();
        }

        /// <summary>
        /// Executes public operation <c>Clone</c>.
        /// </summary>
        public override GeneratorMetadataEntry Clone()
        {
            return new MetadataDictionaryEntry<TKey, TValue>(dictionary);
        }

        /// <summary>
        /// Executes public operation <c>Contains</c>.
        /// </summary>
        public bool Contains(KeyValuePair<TKey, TValue> item)
        {
            return ((ICollection<KeyValuePair<TKey, TValue>>)dictionary).Contains(item);
        }

        /// <summary>
        /// Executes public operation <c>ContainsKey</c>.
        /// </summary>
        public bool ContainsKey(TKey key)
        {
            return ((IDictionary<TKey, TValue>)dictionary).ContainsKey(key);
        }

        /// <summary>
        /// Executes public operation <c>CopyTo</c>.
        /// </summary>
        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            ((ICollection<KeyValuePair<TKey, TValue>>)dictionary).CopyTo(array, arrayIndex);
        }

        /// <summary>
        /// Executes public operation <c>CopyTo</c>.
        /// </summary>
        public void CopyTo(Dictionary<TKey, TValue> other)
        {
            other.AddRange(dictionary);
        }

        /// <summary>
        /// Executes public operation <c>CopyFrom</c>.
        /// </summary>
        public void CopyFrom(Dictionary<TKey, TValue> other)
        {
            dictionary.AddRange(other);
        }

        /// <summary>
        /// Returns computed data from <c>GetEnumerator</c>.
        /// </summary>
        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            return ((IEnumerable<KeyValuePair<TKey, TValue>>)dictionary).GetEnumerator();
        }

        /// <summary>
        /// Merges configuration or metadata via <c>Merge</c>.
        /// </summary>
        public override void Merge(GeneratorMetadataEntry from, in MergeOptions options)
        {
            if (from is MetadataDictionaryEntry<TKey, TValue> dict)
            {
                dictionary.AddRange(dict.dictionary);
            }
        }

        /// <summary>
        /// Removes data or behavior through <c>Remove</c>.
        /// </summary>
        public bool Remove(TKey key)
        {
            return ((IDictionary<TKey, TValue>)dictionary).Remove(key);
        }

        /// <summary>
        /// Removes data or behavior through <c>Remove</c>.
        /// </summary>
        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            return ((ICollection<KeyValuePair<TKey, TValue>>)dictionary).Remove(item);
        }

        /// <summary>
        /// Attempts to resolve data via <c>TryGetValue</c> without throwing.
        /// </summary>
        public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
        {
            return ((IDictionary<TKey, TValue>)dictionary).TryGetValue(key, out value);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)dictionary).GetEnumerator();
        }
    }
}
