using System;

namespace BGCS.Runtime
{
    using System.Diagnostics;

    [DebuggerDisplay("{DebuggerDisplay,nq}")]
    /// <summary>
    /// Strongly typed read-only native pointer wrapper.
    /// </summary>
    /// <typeparam name="T">Element type pointed to by this pointer.</typeparam>
    public readonly unsafe struct ConstPointer<T> : IEquatable<ConstPointer<T>> where T : unmanaged
    {
        /// <summary>
        /// Raw pointer handle.
        /// </summary>
        public readonly T* Handle;

        /// <summary>
        /// Creates a pointer wrapper from a raw typed pointer.
        /// </summary>
        /// <param name="handle">Raw pointer value.</param>
        public ConstPointer(T* handle)
        {
            Handle = handle;
        }

        /// <summary>
        /// Creates a pointer wrapper from a signed native integer address.
        /// </summary>
        /// <param name="handle">Pointer address.</param>
        public ConstPointer(nint handle)
        {
            Handle = (T*)handle;
        }

        /// <summary>
        /// Creates a pointer wrapper from an unsigned native integer address.
        /// </summary>
        /// <param name="handle">Pointer address.</param>
        public ConstPointer(nuint handle)
        {
            Handle = (T*)handle;
        }

        /// <summary>
        /// Gets an element relative to <see cref="Handle"/>.
        /// </summary>
        /// <param name="index">Zero-based element offset.</param>
        public T this[int index]
        {
            get => Handle[index];
        }

        /// <inheritdoc/>
        public override readonly bool Equals(object? obj)
        {
            return obj is ConstPointer<T> pointer && Equals(pointer);
        }

        /// <summary>
        /// Compares this pointer with another pointer by address.
        /// </summary>
        public readonly bool Equals(ConstPointer<T> other)
        {
            return (nint)Handle == (nint)other.Handle;
        }

        /// <inheritdoc/>
        public override readonly int GetHashCode()
        {
            return ((nint)Handle).GetHashCode();
        }

        /// <summary>
        /// Compares pointer addresses for equality.
        /// </summary>
        public static bool operator ==(ConstPointer<T> left, ConstPointer<T> right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Compares pointer addresses for inequality.
        /// </summary>
        public static bool operator !=(ConstPointer<T> left, ConstPointer<T> right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Compares a pointer wrapper and a native signed address.
        /// </summary>
        public static bool operator ==(ConstPointer<T> left, nint right)
        {
            return (nint)left.Handle == right;
        }

        /// <summary>
        /// Compares a pointer wrapper and a native signed address for inequality.
        /// </summary>
        public static bool operator !=(ConstPointer<T> left, nint right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Compares a pointer wrapper and a native unsigned address.
        /// </summary>
        public static bool operator ==(ConstPointer<T> left, nuint right)
        {
            return (nuint)left.Handle == right;
        }

        /// <summary>
        /// Compares a pointer wrapper and a native unsigned address for inequality.
        /// </summary>
        public static bool operator !=(ConstPointer<T> left, nuint right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Compares a pointer wrapper and a raw typed pointer.
        /// </summary>
        public static bool operator ==(ConstPointer<T> left, T* right)
        {
            return left.Handle == right;
        }

        /// <summary>
        /// Compares a pointer wrapper and a raw typed pointer for inequality.
        /// </summary>
        public static bool operator !=(ConstPointer<T> left, T* right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Converts this wrapper to a raw pointer.
        /// </summary>
        public static implicit operator T*(ConstPointer<T> pointer)
        {
            return pointer.Handle;
        }

        /// <summary>
        /// Converts a raw pointer to a wrapper.
        /// </summary>
        public static implicit operator ConstPointer<T>(T* pointer)
        {
            return new(pointer);
        }

        /// <summary>
        /// Converts this wrapper to a signed native integer address.
        /// </summary>
        public static implicit operator nint(ConstPointer<T> pointer)
        {
            return (nint)pointer.Handle;
        }

        /// <summary>
        /// Converts a signed native address to a wrapper.
        /// </summary>
        public static implicit operator ConstPointer<T>(nint pointer)
        {
            return new(pointer);
        }

        /// <summary>
        /// Converts this wrapper to an unsigned native integer address.
        /// </summary>
        public static implicit operator nuint(ConstPointer<T> pointer)
        {
            return (nuint)pointer.Handle;
        }

        /// <summary>
        /// Converts an unsigned native address to a wrapper.
        /// </summary>
        public static implicit operator ConstPointer<T>(nuint pointer)
        {
            return new(pointer);
        }

        /// <summary>
        /// Returns a new pointer moved forward by an element offset.
        /// </summary>
        public static ConstPointer<T> operator +(ConstPointer<T> pointer, int offset)
        {
            return new(pointer.Handle + offset);
        }

        /// <summary>
        /// Returns a new pointer moved backward by an element offset.
        /// </summary>
        public static ConstPointer<T> operator -(ConstPointer<T> pointer, int offset)
        {
            return new(pointer.Handle - offset);
        }

        /// <summary>
        /// Returns a new pointer advanced by one element.
        /// </summary>
        public static ConstPointer<T> operator ++(ConstPointer<T> pointer)
        {
            return new(pointer.Handle + 1);
        }

        /// <summary>
        /// Returns a new pointer decremented by one element.
        /// </summary>
        public static ConstPointer<T> operator --(ConstPointer<T> pointer)
        {
            return new(pointer.Handle - 1);
        }

        private readonly string DebuggerDisplay => string.Format("[0x{0}]", ((nint)Handle).ToString("X"));
    }
}
