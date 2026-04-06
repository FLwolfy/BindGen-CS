namespace BGCS.Core
{
    using System;
    using System.Collections;
    using System.Diagnostics.CodeAnalysis;

    /// <summary>
    /// Defines the public class <c>TrieSet</c>.
    /// </summary>
    public class TrieSet<T> : ICollection<IEnumerable<T>> where T : notnull
    {
        private readonly IEqualityComparer<T> _comparer;
        private readonly TrieNode root;
        private int count;

        /// <summary>
        /// Initializes a new instance of <see cref="TrieSet"/>.
        /// </summary>
        public TrieSet()
        {
            _comparer = EqualityComparer<T>.Default;
            root = new(default, _comparer);
        }

        /// <summary>
        /// Executes public operation <c>TrieSet</c>.
        /// </summary>
        public TrieSet(IEqualityComparer<T> comparer)
        {
            _comparer = comparer;
            root = new(default, _comparer);
        }

        /// <summary>
        /// Defines the public class <c>TrieNode</c>.
        /// </summary>
        public class TrieNode
        {
            /// <summary>
            /// Exposes public member <c>Key</c>.
            /// </summary>
            public readonly T Key;
            /// <summary>
            /// Exposes public member <c>Value</c>.
            /// </summary>
            public IEnumerable<T>? Value;
            /// <summary>
            /// Exposes public member <c>Parent</c>.
            /// </summary>
            public TrieNode? Parent;
            /// <summary>
            /// Exposes public member <c>Children</c>.
            /// </summary>
            public Dictionary<T, TrieNode> Children;
            /// <summary>
            /// Exposes public member <c>IsTerminal</c>.
            /// </summary>
            public bool IsTerminal;

            /// <summary>
            /// Initializes a new instance of <see cref="TrieNode"/>.
            /// </summary>
            public TrieNode(T key, IEqualityComparer<T> comparer)
            {
                Key = key;
                Children = new(comparer);
            }

            /// <summary>
            /// Executes public operation <c>TrieNode</c>.
            /// </summary>
            public TrieNode(T key, TrieNode parent, IEqualityComparer<T> comparer)
            {
                Key = key;
                Parent = parent;
                Children = new(comparer);
            }
        }

        /// <summary>
        /// Exposes public member <c>count</c>.
        /// </summary>
        public int Count => count;

        bool ICollection<IEnumerable<T>>.IsReadOnly => false;

        /// <summary>
        /// Returns computed data from <c>GetEnumerator</c>.
        /// </summary>
        public IEnumerator<IEnumerable<T>> GetEnumerator()
        {
            return GetAllNodes(root).Select(GetFullKey).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// Adds data or behavior through <c>Add</c>.
        /// </summary>
        public void Add(IEnumerable<T> value)
        {
            TrieNode node = root;
            foreach (var key in value)
            {
                node = AddItem(node, key);
            }

            if (node.IsTerminal)
            {
                throw new InvalidOperationException("Key is already in the list");
            }

            node.IsTerminal = true;
            node.Value = value;
            count++;
        }

        /// <summary>
        /// Adds data or behavior through <c>AddRange</c>.
        /// </summary>
        public void AddRange(IEnumerable<IEnumerable<T>> values)
        {
            foreach (IEnumerable<T> key in values)
            {
                Add(key);
            }
        }

        /// <summary>
        /// Adds data or behavior through <c>AddItem</c>.
        /// </summary>
        public TrieNode AddItem(TrieNode node, T key)
        {
            if (!node.Children.TryGetValue(key, out var child))
            {
                child = new(key, node, _comparer);

                node.Children.Add(key, child);
            }

            return child;
        }

        /// <summary>
        /// Executes public operation <c>Clear</c>.
        /// </summary>
        public void Clear()
        {
            root.Children.Clear();
            count = 0;
        }

        /// <summary>
        /// Executes public operation <c>Contains</c>.
        /// </summary>
        public bool Contains(IEnumerable<T> key)
        {
            var node = GetNode(key);

            return node != null && node.IsTerminal;
        }

        /// <summary>
        /// Executes public operation <c>CopyTo</c>.
        /// </summary>
        public void CopyTo(IEnumerable<T>[] array, int arrayIndex) => Array.Copy(GetAllNodes(root).Select(GetFullKey).ToArray(), 0, array, arrayIndex, Count);

        /// <summary>
        /// Removes data or behavior through <c>Remove</c>.
        /// </summary>
        public bool Remove(IEnumerable<T> key)
        {
            TrieNode? node = GetNode(key);

            if (node == null)
            {
                return false;
            }

            if (!node.IsTerminal)
            {
                return false;
            }

            RemoveNode(node);

            return true;
        }

        /// <summary>
        /// Returns computed data from <c>GetNode</c>.
        /// </summary>
        public TrieNode? GetNode(IEnumerable<T> key)
        {
            var node = root;

            foreach (T item in key)
            {
                if (!node.Children.TryGetValue(item, out node))
                {
                    return null;
                }
            }

            return node;
        }

        /// <summary>
        /// Attempts to resolve data via <c>TryGetNode</c> without throwing.
        /// </summary>
        public bool TryGetNode(IEnumerable<T> key, [NotNullWhen(true)] out TrieNode? node)
        {
            node = GetNode(key);

            return node != null && node.IsTerminal;
        }

        /// <summary>
        /// Removes data or behavior through <c>RemoveNode</c>.
        /// </summary>
        public void RemoveNode(TrieNode node)
        {
            Remove(node);
            count--;
        }

        /// <summary>
        /// Removes data or behavior through <c>Remove</c>.
        /// </summary>
        public void Remove(TrieNode node)
        {
            while (true)
            {
                node.IsTerminal = false;

                if (node.Children.Count == 0 && node.Parent != null)
                {
                    Remove(node.Parent, node.Key);

                    if (!node.Parent.IsTerminal)
                    {
                        node = node.Parent;
                        continue;
                    }
                }

                break;
            }
        }

        /// <summary>
        /// Removes data or behavior through <c>Remove</c>.
        /// </summary>
        public void Remove(TrieNode node, T key)
        {
            foreach (var trieNode in node.Children)
            {
                if (_comparer.Equals(key, trieNode.Key))
                {
                    node.Children.Remove(trieNode.Key);
                    return;
                }
            }
        }

        /// <summary>
        /// Executes public operation <c>FindLargestMatch</c>.
        /// </summary>
        public ReadOnlySpan<T> FindLargestMatch(ReadOnlySpan<T> match)
        {
            if (match.Length == 0)
            {
                return match;
            }

            int lastMatch = 0;
            TrieNode last = root;
            for (int i = 0; i < match.Length; i++)
            {
                var c = match[i];

                if (last.Children.TryGetValue(c, out var child))
                {
                    if (child.IsTerminal)
                    {
                        lastMatch = i;
                    }
                }
                else
                {
                    break;
                }
            }

            return match[..(lastMatch + 1)];
        }

        /// <summary>
        /// Executes public operation <c>FindSmallestMatch</c>.
        /// </summary>
        public ReadOnlySpan<T> FindSmallestMatch(ReadOnlySpan<T> match)
        {
            if (match.Length == 0)
            {
                return match;
            }

            TrieNode last = root;
            for (int i = 0; i < match.Length; i++)
            {
                var c = match[i];

                if (last.Children.TryGetValue(c, out var child))
                {
                    if (child.IsTerminal)
                    {
                        return match[..(i + 1)];
                    }
                }
                else
                {
                    break;
                }
            }

            return match[..0];
        }

        /// <summary>
        /// Gets items by key prefix.
        /// </summary>
        /// <param name="prefix">Key prefix.</param>
        /// <returns>Collection of <see cref="T"/> items.</returns>
        public IEnumerable<IEnumerable<T>> GetByPrefix(IEnumerable<T> prefix)
        {
            if (prefix == null) throw new ArgumentNullException(nameof(prefix));

            var node = root;

            foreach (var item in prefix)
            {
                if (!node.Children.TryGetValue(item, out node))
                {
                    return Enumerable.Empty<IEnumerable<T>>();
                }
            }

            return GetByPrefix(node);
        }

        private static IEnumerable<T> GetFullKey(TrieNode node)
        {
            return node.Value;
        }

        private static IEnumerable<TrieNode> GetAllNodes(TrieNode node)
        {
            foreach (var child in node.Children)
            {
                if (child.Value.IsTerminal)
                {
                    yield return child.Value;
                }

                foreach (var item in GetAllNodes(child.Value))
                {
                    if (item.IsTerminal)
                    {
                        yield return item;
                    }
                }
            }
        }

        private static IEnumerable<IEnumerable<T>> GetByPrefix(TrieNode node)
        {
            var stack = new Stack<TrieNode>();
            var current = node;

            while (stack.Count > 0 || current != null)
            {
                if (current != null)
                {
                    if (current.IsTerminal)
                    {
                        yield return GetFullKey(current);
                    }

                    using (var enumerator = current.Children.GetEnumerator())
                    {
                        current = enumerator.MoveNext() ? enumerator.Current.Value : null;

                        while (enumerator.MoveNext())
                        {
                            stack.Push(enumerator.Current.Value);
                        }
                    }
                }
                else
                {
                    current = stack.Pop();
                }
            }
        }
    }
}
