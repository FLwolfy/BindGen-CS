using System;

namespace BGCS.Runtime
{
    using System.Runtime.InteropServices;

    /// <summary>
    /// Lightweight pointer-based iterator for unmanaged buffers.
    /// </summary>
    /// <typeparam name = "T">Element type in the underlying buffer.</typeparam>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct Iterator<T>
        where T : unmanaged
    {
        /// <summary>
        /// Base pointer of the iterated buffer.
        /// </summary>
        public T* ptr;
        /// <summary>
        /// Current element offset from <see cref = "ptr"/>.
        /// </summary>
        public nuint index;
        /// <summary>
        /// Initializes an iterator for a native buffer.
        /// </summary>
        /// <param name = "ptr">Base pointer.</param>
        /// <param name = "index">Initial element offset.</param>
        public Iterator(
            T* ptr,
            nuint index = 0
        ) {
            this.ptr = ptr;
            this.index = index;
        }

        /// <summary>
        /// Gets a pointer to the current element.
        /// </summary>
        public T* current => ptr + index;

        /// <summary>
        /// Advances the iterator by one element.
        /// </summary>
        public void MoveNext()
        {
            index++;
        }

        /// <summary>
        /// Compares borrowed native buffer addresses and element offsets without reading buffer contents.
        /// </summary>
        /// <param name="other">
        /// The iterator value to compare with this value.
        /// </param>
        /// <returns>
        /// True only when both base pointers and offsets are equal.
        /// </returns>
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
        /// Compares native iterator positions without taking buffer ownership.
        /// </summary>
        /// <param name="left">
        /// The first borrowed iterator value.
        /// </param>
        /// <param name="right">
        /// The second borrowed iterator value.
        /// </param>
        /// <returns>
        /// True when both base pointers and element offsets are equal.
        /// </returns>
        public static bool operator ==(
            Iterator<T> left,
            Iterator<T> right
        ) {
            return left.Equals(right);
        }

        /// <summary>
        /// Compares native iterator positions without taking buffer ownership.
        /// </summary>
        /// <param name="left">
        /// The first borrowed iterator value.
        /// </param>
        /// <param name="right">
        /// The second borrowed iterator value.
        /// </param>
        /// <returns>
        /// True when the base pointers or element offsets differ.
        /// </returns>
        public static bool operator !=(
            Iterator<T> left,
            Iterator<T> right
        ) {
            return !(left == right);
        }
    }
}
