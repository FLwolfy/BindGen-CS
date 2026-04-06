namespace BGCS.Metadata
{
    using BGCS.Core;
    using BGCS.Core.Collections;
    using System.Collections;

    /// <summary>
    /// Defines the public class <c>MetadataListEntry</c>.
    /// </summary>
    public class MetadataListEntry<T> : GeneratorMetadataEntry, IList<T>
    {
        private readonly List<T> values = [];

        /// <summary>
        /// Initializes a new instance of <see cref="MetadataListEntry"/>.
        /// </summary>
        public MetadataListEntry()
        {
        }

        /// <summary>
        /// Executes public operation <c>MetadataListEntry</c>.
        /// </summary>
        public MetadataListEntry(IEnumerable<T> values)
        {
            this.values.AddRange(values);
        }

        /// <summary>
        /// Executes public operation <c>Member</c>.
        /// </summary>
        public T this[int index] { get => ((IList<T>)values)[index]; set => ((IList<T>)values)[index] = value; }

        /// <summary>
        /// Executes public operation <c>Member</c>.
        /// </summary>
        public int Count => ((ICollection<T>)values).Count;

        /// <summary>
        /// Executes public operation <c>Member</c>.
        /// </summary>
        public bool IsReadOnly => ((ICollection<T>)values).IsReadOnly;

        /// <summary>
        /// Exposes public member <c>values</c>.
        /// </summary>
        public List<T> Values => values;

        /// <summary>
        /// Adds data or behavior through <c>Add</c>.
        /// </summary>
        public void Add(T item)
        {
            ((ICollection<T>)values).Add(item);
        }

        /// <summary>
        /// Executes public operation <c>Clear</c>.
        /// </summary>
        public void Clear()
        {
            ((ICollection<T>)values).Clear();
        }

        /// <summary>
        /// Executes public operation <c>Clone</c>.
        /// </summary>
        public override GeneratorMetadataEntry Clone()
        {
            if (typeof(T).IsAssignableTo(typeof(ICloneable<T>)))
            {
                return new MetadataListEntry<T>(values.Select(static x => ((ICloneable<T>)x!).Clone()));
            }
            return new MetadataListEntry<T>(values);
        }

        /// <summary>
        /// Executes public operation <c>Contains</c>.
        /// </summary>
        public bool Contains(T item)
        {
            return ((ICollection<T>)values).Contains(item);
        }

        /// <summary>
        /// Executes public operation <c>CopyTo</c>.
        /// </summary>
        public void CopyTo(T[] array, int arrayIndex)
        {
            ((ICollection<T>)values).CopyTo(array, arrayIndex);
        }

        /// <summary>
        /// Executes public operation <c>CopyTo</c>.
        /// </summary>
        public void CopyTo(List<T> other)
        {
            other.AddRange(values);
        }

        /// <summary>
        /// Executes public operation <c>CopyTo</c>.
        /// </summary>
        public void CopyTo(HashSet<T> other)
        {
            other.AddRange(values);
        }

        /// <summary>
        /// Returns computed data from <c>GetEnumerator</c>.
        /// </summary>
        public IEnumerator<T> GetEnumerator()
        {
            return ((IEnumerable<T>)values).GetEnumerator();
        }

        /// <summary>
        /// Executes public operation <c>IndexOf</c>.
        /// </summary>
        public int IndexOf(T item)
        {
            return ((IList<T>)values).IndexOf(item);
        }

        /// <summary>
        /// Executes public operation <c>Insert</c>.
        /// </summary>
        public void Insert(int index, T item)
        {
            ((IList<T>)values).Insert(index, item);
        }

        /// <summary>
        /// Merges configuration or metadata via <c>Merge</c>.
        /// </summary>
        public override void Merge(GeneratorMetadataEntry from, in MergeOptions options)
        {
            if (from is MetadataListEntry<T> list)
            {
                values.AddRange(list);
            }
        }

        /// <summary>
        /// Removes data or behavior through <c>Remove</c>.
        /// </summary>
        public bool Remove(T item)
        {
            return ((ICollection<T>)values).Remove(item);
        }

        /// <summary>
        /// Removes data or behavior through <c>RemoveAt</c>.
        /// </summary>
        public void RemoveAt(int index)
        {
            ((IList<T>)values).RemoveAt(index);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)values).GetEnumerator();
        }
    }
}
