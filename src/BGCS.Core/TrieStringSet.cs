namespace BGCS.Core
{
    using System;
    using System.Collections;
    using System.Diagnostics.CodeAnalysis;
    using System.Text;

    /// <summary>
    /// Defines the public class <c>TrieStringSet</c>.
    /// </summary>
    public class TrieStringSet : ICollection<string>
    {
        private readonly IEqualityComparer<char> _comparer;
        private readonly TrieSetNode root;
        private int count;

        /// <summary>
        /// Initializes a new instance of <see cref="TrieStringSet"/>.
        /// </summary>
        public TrieStringSet()
        {
            _comparer = EqualityComparer<char>.Default;
            root = new('\0', _comparer);
        }

        /// <summary>
        /// Executes public operation <c>TrieStringSet</c>.
        /// </summary>
        public TrieStringSet(IEqualityComparer<char> comparer)
        {
            _comparer = comparer;
            root = new('\0', _comparer);
        }

        /// <summary>
        /// Defines the public class <c>TrieSetNode</c>.
        /// </summary>
        public class TrieSetNode
        {
            /// <summary>
            /// Exposes public member <c>Key</c>.
            /// </summary>
            public readonly char Key;
            /// <summary>
            /// Exposes public member <c>Parent</c>.
            /// </summary>
            public TrieSetNode? Parent;
            /// <summary>
            /// Exposes public member <c>Children</c>.
            /// </summary>
            public Dictionary<char, TrieSetNode> Children;
            /// <summary>
            /// Exposes public member <c>IsTerminal</c>.
            /// </summary>
            public bool IsTerminal;

            /// <summary>
            /// Initializes a new instance of <see cref="TrieSetNode"/>.
            /// </summary>
            public TrieSetNode(char key, IEqualityComparer<char> comparer)
            {
                Key = key;
                Children = new(comparer);
            }

            /// <summary>
            /// Executes public operation <c>TrieSetNode</c>.
            /// </summary>
            public TrieSetNode(char key, TrieSetNode parent, IEqualityComparer<char> comparer)
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

        bool ICollection<string>.IsReadOnly => false;

        /// <summary>
        /// Returns computed data from <c>GetEnumerator</c>.
        /// </summary>
        public IEnumerator<string> GetEnumerator()
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
        public void Add(string key)
        {
            TrieSetNode node = root;
            for (int i = 0; i < key.Length; i++)
            {
                node = AddItem(node, key[i]);
            }

            if (node.IsTerminal)
            {
                throw new InvalidOperationException("Key is already in the list");
            }

            node.IsTerminal = true;
            count++;
        }

        /// <summary>
        /// Adds data or behavior through <c>AddRange</c>.
        /// </summary>
        public void AddRange(IEnumerable<string> keys)
        {
            foreach (string key in keys)
            {
                Add(key);
            }
        }

        /// <summary>
        /// Adds data or behavior through <c>AddItem</c>.
        /// </summary>
        public TrieSetNode AddItem(TrieSetNode node, char key)
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
        public bool Contains(string key)
        {
            var node = GetNode(key);

            return node != null && node.IsTerminal;
        }

        /// <summary>
        /// Executes public operation <c>CopyTo</c>.
        /// </summary>
        public void CopyTo(string[] array, int arrayIndex) => Array.Copy(GetAllNodes(root).Select(GetFullKey).ToArray(), 0, array, arrayIndex, Count);

        /// <summary>
        /// Removes data or behavior through <c>Remove</c>.
        /// </summary>
        public bool Remove(string key)
        {
            TrieSetNode? node = GetNode(key);

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
        public TrieSetNode? GetNode(string key)
        {
            var node = root;

            for (int i = 0; i < key.Length; i++)
            {
                char item = key[i];
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
        public bool TryGetNode(string key, [NotNullWhen(true)] out TrieSetNode? node)
        {
            node = GetNode(key);

            return node != null && node.IsTerminal;
        }

        /// <summary>
        /// Removes data or behavior through <c>RemoveNode</c>.
        /// </summary>
        public void RemoveNode(TrieSetNode node)
        {
            Remove(node);
            count--;
        }

        /// <summary>
        /// Removes data or behavior through <c>Remove</c>.
        /// </summary>
        public void Remove(TrieSetNode node)
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
        public void Remove(TrieSetNode node, char key)
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
        public ReadOnlySpan<char> FindLargestMatch(ReadOnlySpan<char> match)
        {
            if (match.Length == 0)
            {
                return match;
            }

            int lastMatch = 0;
            TrieSetNode last = root;
            for (int i = 0; i < match.Length; i++)
            {
                if (last.Children.TryGetValue(match[i], out var child))
                {
                    last = child;
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
        public ReadOnlySpan<char> FindSmallestMatch(ReadOnlySpan<char> match)
        {
            if (match.Length == 0)
            {
                return match;
            }

            TrieSetNode last = root;
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

        private static string GetFullKey(TrieSetNode node)
        {
            StringBuilder sb = new();

            var n = node;

            while (n != null && n.Parent != null)
            {
                sb.Append(n.Key);
                n = n.Parent;
            }

            sb.Reverse();

            return sb.ToString();
        }

        private static IEnumerable<TrieSetNode> GetAllNodes(TrieSetNode node)
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
    }
}
