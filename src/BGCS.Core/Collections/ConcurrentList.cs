namespace BGCS.Core.Collections
{
    using System.Collections;

    /// <summary>
    /// Defines the public class <c>ConcurrentList</c>.
    /// </summary>
    public class ConcurrentList<T> : IList<T>, IReadOnlyList<T>
    {
        private readonly List<T> _list;
        private readonly object _lock = new();

        /// <summary>
        /// Initializes a new instance of <see cref="ConcurrentList"/>.
        /// </summary>
        public ConcurrentList()
        {
            _list = new();
        }

        /// <summary>
        /// Executes public operation <c>ConcurrentList</c>.
        /// </summary>
        public ConcurrentList(int capacity)
        {
            _list = new(capacity);
        }

        /// <summary>
        /// Executes public operation <c>ConcurrentList</c>.
        /// </summary>
        public ConcurrentList(IEnumerable<T> values)
        {
            _list = new(values);
        }

        /// <summary>
        /// Exposes public member <c>index]</c>.
        /// </summary>
        public T this[int index]
        {
            get
            {
                lock (_lock)
                {
                    return _list[index];
                }
            }
            set
            {
                lock (_lock)
                {
                    _list[index] = value;
                }
            }
        }

        /// <summary>
        /// Exposes public member <c>Count</c>.
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _list.Count;
                }
            }
        }

        /// <summary>
        /// Gets <c>IsReadOnly</c>.
        /// </summary>
        public bool IsReadOnly { get; } = false;

        /// <summary>
        /// Exposes public member <c>_lock</c>.
        /// </summary>
        public object SyncObject => _lock;

        /// <summary>
        /// Adds data or behavior through <c>Add</c>.
        /// </summary>
        public void Add(T item)
        {
            lock (_lock)
            {
                _list.Add(item);
            }
        }

        /// <summary>
        /// Executes public operation <c>Clear</c>.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _list.Clear();
            }
        }

        /// <summary>
        /// Executes public operation <c>Contains</c>.
        /// </summary>
        public bool Contains(T item)
        {
            lock (_lock)
            {
                return _list.Contains(item);
            }
        }

        /// <summary>
        /// Executes public operation <c>CopyTo</c>.
        /// </summary>
        public void CopyTo(T[] array, int arrayIndex)
        {
            lock (_lock)
            {
                _list.CopyTo(array, arrayIndex);
            }
        }

        /// <summary>
        /// Returns computed data from <c>GetEnumerator</c>.
        /// </summary>
        public IEnumerator<T> GetEnumerator()
        {
            lock (_lock)
            {
                return _list.GetEnumerator();
            }
        }

        /// <summary>
        /// Executes public operation <c>IndexOf</c>.
        /// </summary>
        public int IndexOf(T item)
        {
            lock (_lock)
            {
                return _list.IndexOf(item);
            }
        }

        /// <summary>
        /// Executes public operation <c>Insert</c>.
        /// </summary>
        public void Insert(int index, T item)
        {
            lock (_lock)
            {
                _list.Insert(index, item);
            }
        }

        /// <summary>
        /// Removes data or behavior through <c>Remove</c>.
        /// </summary>
        public bool Remove(T item)
        {
            lock (_lock)
            {
                return _list.Remove(item);
            }
        }

        /// <summary>
        /// Removes data or behavior through <c>RemoveAt</c>.
        /// </summary>
        public void RemoveAt(int index)
        {
            lock (_lock)
            {
                _list.RemoveAt(index);
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            lock (_lock)
            {
                return _list.GetEnumerator();
            }
        }
    }
}
