using System;

namespace BGCS.Runtime
{
    using System.Runtime.InteropServices;

    [StructLayout(LayoutKind.Sequential)]
    /// <summary>
    /// Lightweight pointer-based iterator for unmanaged buffers.
    /// </summary>
    /// <typeparam name="T">Element type in the underlying buffer.</typeparam>
    public unsafe struct Iterator<T> where T : unmanaged
    {
        /// <summary>
        /// Base pointer of the iterated buffer.
        /// </summary>
        public T* ptr;
        /// <summary>
        /// Current element offset from <see cref="ptr"/>.
        /// </summary>
        public nuint index;

        /// <summary>
        /// Initializes an iterator for a native buffer.
        /// </summary>
        /// <param name="ptr">Base pointer.</param>
        /// <param name="index">Initial element offset.</param>
        public Iterator(T* ptr, nuint index = 0)
        {
            this.ptr = ptr;
            this.index = index;
        }

        /// <summary>
        /// Gets a pointer to the current element.
        /// </summary>
        public T* Current => ptr + index;

        /// <summary>
        /// Advances the iterator by one element.
        /// </summary>
        public void MoveNext()
        {
            index++;
        }

        /// <summary>
        /// Compares two iterators by base pointer and index.
        /// </summary>
        public readonly bool Equals(Iterator<T> other)
        {
            return ptr == other.ptr && index == other.index;
        }

        /// <inheritdoc/>
        public override readonly bool Equals(object? obj)
        {
            if (obj is Iterator<T> other)
            {
                return Equals(other);
            }
            return false;
        }

        /// <inheritdoc/>
        public override readonly int GetHashCode()
        {
            return ((IntPtr)ptr).GetHashCode() ^ index.GetHashCode();
        }

        /// <summary>
        /// Compares two iterators for equality.
        /// </summary>
        public static bool operator ==(Iterator<T> left, Iterator<T> right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Compares two iterators for inequality.
        /// </summary>
        public static bool operator !=(Iterator<T> left, Iterator<T> right)
        {
            return !(left == right);
        }
    }
}
