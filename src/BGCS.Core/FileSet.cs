namespace BGCS.Core
{
    using System;
    using System.Collections;
    using System.Collections.Frozen;
    using System.Collections.Generic;

    /// <summary>
    /// Defines the public class <c>FileSet</c>.
    /// </summary>
    public class FileSet : ISet<string>, IReadOnlySet<string>, IReadOnlyCollection<string>, ICollection
    {
        private readonly FrozenSet<string> set;

        /// <summary>
        /// Initializes a new instance of <see cref="FileSet"/>.
        /// </summary>
        public FileSet(FrozenSet<string> set)
        {
            this.set = set;
        }

        /// <summary>
        /// Executes public operation <c>FileSet</c>.
        /// </summary>
        public FileSet(IEnumerable<string> set)
        {
            this.set = set.ToFrozenSet();
        }

        /// <summary>
        /// Executes public operation <c>Member</c>.
        /// </summary>
        public int Count => ((ICollection)set).Count;

        /// <summary>
        /// Executes public operation <c>Member</c>.
        /// </summary>
        public bool IsSynchronized => ((ICollection)set).IsSynchronized;

        /// <summary>
        /// Executes public operation <c>Member</c>.
        /// </summary>
        public object SyncRoot => ((ICollection)set).SyncRoot;

        /// <summary>
        /// Executes public operation <c>Member</c>.
        /// </summary>
        public bool IsReadOnly => ((ICollection<string>)set).IsReadOnly;

        /// <summary>
        /// Adds data or behavior through <c>Add</c>.
        /// </summary>
        public bool Add(string item)
        {
            return ((ISet<string>)set).Add(item);
        }

        /// <summary>
        /// Executes public operation <c>Clear</c>.
        /// </summary>
        public void Clear()
        {
            ((ICollection<string>)set).Clear();
        }

        /// <summary>
        /// Executes public operation <c>Contains</c>.
        /// </summary>
        public bool Contains(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            path = PathHelper.GetPath(path);
            return set.Contains(path);
        }

        /// <summary>
        /// Executes public operation <c>CopyTo</c>.
        /// </summary>
        public void CopyTo(Array array, int index)
        {
            ((ICollection)set).CopyTo(array, index);
        }

        /// <summary>
        /// Executes public operation <c>CopyTo</c>.
        /// </summary>
        public void CopyTo(string[] array, int arrayIndex)
        {
            ((ICollection<string>)set).CopyTo(array, arrayIndex);
        }

        /// <summary>
        /// Executes public operation <c>ExceptWith</c>.
        /// </summary>
        public void ExceptWith(IEnumerable<string> other)
        {
            ((ISet<string>)set).ExceptWith(other);
        }

        /// <summary>
        /// Returns computed data from <c>GetEnumerator</c>.
        /// </summary>
        public IEnumerator GetEnumerator()
        {
            return ((IEnumerable)set).GetEnumerator();
        }

        /// <summary>
        /// Executes public operation <c>IntersectWith</c>.
        /// </summary>
        public void IntersectWith(IEnumerable<string> other)
        {
            ((ISet<string>)set).IntersectWith(other);
        }

        /// <summary>
        /// Executes public operation <c>IsProperSubsetOf</c>.
        /// </summary>
        public bool IsProperSubsetOf(IEnumerable<string> other)
        {
            return ((ISet<string>)set).IsProperSubsetOf(other);
        }

        /// <summary>
        /// Executes public operation <c>IsProperSupersetOf</c>.
        /// </summary>
        public bool IsProperSupersetOf(IEnumerable<string> other)
        {
            return ((ISet<string>)set).IsProperSupersetOf(other);
        }

        /// <summary>
        /// Executes public operation <c>IsSubsetOf</c>.
        /// </summary>
        public bool IsSubsetOf(IEnumerable<string> other)
        {
            return ((ISet<string>)set).IsSubsetOf(other);
        }

        /// <summary>
        /// Executes public operation <c>IsSupersetOf</c>.
        /// </summary>
        public bool IsSupersetOf(IEnumerable<string> other)
        {
            return ((ISet<string>)set).IsSupersetOf(other);
        }

        /// <summary>
        /// Executes public operation <c>Overlaps</c>.
        /// </summary>
        public bool Overlaps(IEnumerable<string> other)
        {
            return ((ISet<string>)set).Overlaps(other);
        }

        /// <summary>
        /// Removes data or behavior through <c>Remove</c>.
        /// </summary>
        public bool Remove(string item)
        {
            return ((ICollection<string>)set).Remove(item);
        }

        /// <summary>
        /// Executes public operation <c>SetEquals</c>.
        /// </summary>
        public bool SetEquals(IEnumerable<string> other)
        {
            return ((ISet<string>)set).SetEquals(other);
        }

        /// <summary>
        /// Executes public operation <c>SymmetricExceptWith</c>.
        /// </summary>
        public void SymmetricExceptWith(IEnumerable<string> other)
        {
            ((ISet<string>)set).SymmetricExceptWith(other);
        }

        /// <summary>
        /// Executes public operation <c>UnionWith</c>.
        /// </summary>
        public void UnionWith(IEnumerable<string> other)
        {
            ((ISet<string>)set).UnionWith(other);
        }

        void ICollection<string>.Add(string item)
        {
            ((ICollection<string>)set).Add(item);
        }

        IEnumerator<string> IEnumerable<string>.GetEnumerator()
        {
            return ((IEnumerable<string>)set).GetEnumerator();
        }
    }
}
