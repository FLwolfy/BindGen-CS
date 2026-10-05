using System;

namespace BGCS.Runtime
{
    using System.Diagnostics;

    /// <summary>
    /// Borrows a typed native address without allocating, freeing, pinning, or validating the pointed-to storage.
    /// </summary>
    /// <typeparam name="T">
    /// The unmanaged element whose size controls pointer arithmetic.
    /// </typeparam>
    [DebuggerDisplay("{debuggerDisplay,nq}")]
    public readonly unsafe struct ConstPointer<T> : IEquatable<ConstPointer<T>> where T : unmanaged
    {
        /// <summary>
        /// The borrowed native address; its allocation, accessibility, and lifetime remain the caller's responsibility.
        /// </summary>
        public readonly T* handle;
        /// <summary>
        /// Wraps a borrowed native address without checking its allocation or lifetime.
        /// </summary>
        /// <param name="handle">
        /// The native address; zero is retained as a null pointer.
        /// </param>
        public ConstPointer(T* handle)
        {
            this.handle = handle;
        }

        /// <summary>
        /// Wraps a borrowed native address without checking its allocation or lifetime.
        /// </summary>
        /// <param name="handle">
        /// The native address; zero is retained as a null pointer.
        /// </param>
        public ConstPointer(nint handle)
        {
            this.handle = (T*)handle;
        }

        /// <summary>
        /// Wraps a borrowed native address without checking its allocation or lifetime.
        /// </summary>
        /// <param name="handle">
        /// The native address; zero is retained as a null pointer.
        /// </param>
        public ConstPointer(nuint handle)
        {
            this.handle = (T*)handle;
        }

        /// <summary>
        /// Provides unchecked read access at an element offset; the caller must ensure valid storage for the complete access.
        /// </summary>
        /// <param name="index">
        /// The signed offset in T-sized elements; bounds are not checked.
        /// </param>
        public T this[int index] { get => this.handle[index]; }

        /// <inheritdoc/>
        public override readonly bool Equals(object? obj)
        {
            return obj is ConstPointer<T> pointer && Equals(pointer);
        }

        /// <summary>
        /// Compares native addresses without dereferencing either pointer.
        /// </summary>
        /// <param name="other">
        /// The borrowed pointer whose address is compared.
        /// </param>
        /// <returns>
        /// True when the addresses are identical, including two null addresses; otherwise false.
        /// </returns>
        public readonly bool Equals(ConstPointer<T> other)
        {
            return (nint)this.handle == (nint)other.handle;
        }

        /// <inheritdoc/>
        public override readonly int GetHashCode()
        {
            return ((nint)this.handle).GetHashCode();
        }

        /// <summary>
        /// Compares native addresses without dereferencing either operand.
        /// </summary>
        /// <param name="left">
        /// The borrowed typed address.
        /// </param>
        /// <param name="right">
        /// The borrowed typed address to compare.
        /// </param>
        /// <returns>
        /// True when the addresses are identical; otherwise false.
        /// </returns>
        public static bool operator ==(
            ConstPointer<T> left,
            ConstPointer<T> right
        ) {
            return left.Equals(right);
        }

        /// <summary>
        /// Compares native addresses without dereferencing either operand.
        /// </summary>
        /// <param name="left">
        /// The borrowed typed address.
        /// </param>
        /// <param name="right">
        /// The borrowed typed address to compare.
        /// </param>
        /// <returns>
        /// True when the addresses are different; otherwise false.
        /// </returns>
        public static bool operator !=(
            ConstPointer<T> left,
            ConstPointer<T> right
        ) {
            return !(left == right);
        }

        /// <summary>
        /// Compares native addresses without dereferencing either operand.
        /// </summary>
        /// <param name="left">
        /// The borrowed typed address.
        /// </param>
        /// <param name="right">
        /// The native address to compare.
        /// </param>
        /// <returns>
        /// True when the addresses are identical; otherwise false.
        /// </returns>
        public static bool operator ==(
            ConstPointer<T> left,
            nint right
        ) {
            return (nint)left.handle == right;
        }

        /// <summary>
        /// Compares native addresses without dereferencing either operand.
        /// </summary>
        /// <param name="left">
        /// The borrowed typed address.
        /// </param>
        /// <param name="right">
        /// The native address to compare.
        /// </param>
        /// <returns>
        /// True when the addresses are different; otherwise false.
        /// </returns>
        public static bool operator !=(
            ConstPointer<T> left,
            nint right
        ) {
            return !(left == right);
        }

        /// <summary>
        /// Compares native addresses without dereferencing either operand.
        /// </summary>
        /// <param name="left">
        /// The borrowed typed address.
        /// </param>
        /// <param name="right">
        /// The native address to compare.
        /// </param>
        /// <returns>
        /// True when the addresses are identical; otherwise false.
        /// </returns>
        public static bool operator ==(
            ConstPointer<T> left,
            nuint right
        ) {
            return (nuint)left.handle == right;
        }

        /// <summary>
        /// Compares native addresses without dereferencing either operand.
        /// </summary>
        /// <param name="left">
        /// The borrowed typed address.
        /// </param>
        /// <param name="right">
        /// The native address to compare.
        /// </param>
        /// <returns>
        /// True when the addresses are different; otherwise false.
        /// </returns>
        public static bool operator !=(
            ConstPointer<T> left,
            nuint right
        ) {
            return !(left == right);
        }

        /// <summary>
        /// Compares native addresses without dereferencing either operand.
        /// </summary>
        /// <param name="left">
        /// The borrowed typed address.
        /// </param>
        /// <param name="right">
        /// The native address to compare.
        /// </param>
        /// <returns>
        /// True when the addresses are identical; otherwise false.
        /// </returns>
        public static bool operator ==(
            ConstPointer<T> left,
            T* right
        ) {
            return left.handle == right;
        }

        /// <summary>
        /// Compares native addresses without dereferencing either operand.
        /// </summary>
        /// <param name="left">
        /// The borrowed typed address.
        /// </param>
        /// <param name="right">
        /// The native address to compare.
        /// </param>
        /// <returns>
        /// True when the addresses are different; otherwise false.
        /// </returns>
        public static bool operator !=(
            ConstPointer<T> left,
            T* right
        ) {
            return !(left == right);
        }

        /// <summary>
        /// Returns the borrowed native address without transferring ownership or accessing its storage.
        /// </summary>
        /// <param name="pointer">
        /// The borrowed address wrapper.
        /// </param>
        /// <returns>
        /// The same native address, including zero for a null pointer.
        /// </returns>
        public static implicit operator T*(ConstPointer<T> pointer)
        {
            return pointer.handle;
        }

        /// <summary>
        /// Wraps a borrowed native address without checking its allocation or lifetime.
        /// </summary>
        /// <param name="pointer">
        /// The native address; zero is retained as a null pointer.
        /// </param>
        /// <returns>
        /// A wrapper borrowing the same storage; it does not allocate, free, or pin memory.
        /// </returns>
        public static implicit operator ConstPointer<T>(T* pointer)
        {
            return new(pointer);
        }

        /// <summary>
        /// Returns the borrowed native address without transferring ownership or accessing its storage.
        /// </summary>
        /// <param name="pointer">
        /// The borrowed address wrapper.
        /// </param>
        /// <returns>
        /// The same native address, including zero for a null pointer.
        /// </returns>
        public static implicit operator nint(ConstPointer<T> pointer)
        {
            return (nint)pointer.handle;
        }

        /// <summary>
        /// Wraps a borrowed native address without checking its allocation or lifetime.
        /// </summary>
        /// <param name="pointer">
        /// The native address; zero is retained as a null pointer.
        /// </param>
        /// <returns>
        /// A wrapper borrowing the same storage; it does not allocate, free, or pin memory.
        /// </returns>
        public static implicit operator ConstPointer<T>(nint pointer)
        {
            return new(pointer);
        }

        /// <summary>
        /// Returns the borrowed native address without transferring ownership or accessing its storage.
        /// </summary>
        /// <param name="pointer">
        /// The borrowed address wrapper.
        /// </param>
        /// <returns>
        /// The same native address, including zero for a null pointer.
        /// </returns>
        public static implicit operator nuint(ConstPointer<T> pointer)
        {
            return (nuint)pointer.handle;
        }

        /// <summary>
        /// Wraps a borrowed native address without checking its allocation or lifetime.
        /// </summary>
        /// <param name="pointer">
        /// The native address; zero is retained as a null pointer.
        /// </param>
        /// <returns>
        /// A wrapper borrowing the same storage; it does not allocate, free, or pin memory.
        /// </returns>
        public static implicit operator ConstPointer<T>(nuint pointer)
        {
            return new(pointer);
        }

        /// <summary>
        /// Computes a borrowed address displaced forward in T-sized elements without bounds checking.
        /// </summary>
        /// <param name="pointer">
        /// The base borrowed address.
        /// </param>
        /// <param name="offset">
        /// The signed displacement in elements.
        /// </param>
        /// <returns>
        /// A wrapper borrowing the computed address; its validity remains the caller's responsibility.
        /// </returns>
        public static ConstPointer<T> operator +(
            ConstPointer<T> pointer,
            int offset
        ) {
            return new(pointer.handle + offset);
        }

        /// <summary>
        /// Computes a borrowed address displaced backward in T-sized elements without bounds checking.
        /// </summary>
        /// <param name="pointer">
        /// The base borrowed address.
        /// </param>
        /// <param name="offset">
        /// The signed displacement in elements.
        /// </param>
        /// <returns>
        /// A wrapper borrowing the computed address; its validity remains the caller's responsibility.
        /// </returns>
        public static ConstPointer<T> operator -(
            ConstPointer<T> pointer,
            int offset
        ) {
            return new(pointer.handle - offset);
        }

        /// <summary>
        /// Computes a borrowed address displaced one T-sized element forward without accessing storage.
        /// </summary>
        /// <param name="pointer">
        /// The base borrowed address.
        /// </param>
        /// <returns>
        /// A wrapper borrowing the computed address; no allocation or ownership transfer occurs.
        /// </returns>
        public static ConstPointer<T> operator ++(ConstPointer<T> pointer)
        {
            return new(pointer.handle + 1);
        }

        /// <summary>
        /// Computes a borrowed address displaced one T-sized element backward without accessing storage.
        /// </summary>
        /// <param name="pointer">
        /// The base borrowed address.
        /// </param>
        /// <returns>
        /// A wrapper borrowing the computed address; no allocation or ownership transfer occurs.
        /// </returns>
        public static ConstPointer<T> operator --(ConstPointer<T> pointer)
        {
            return new(pointer.handle - 1);
        }

        private readonly string debuggerDisplay => string.Format("[0x{0}]", ((nint)this.handle).ToString("X"));
    }
}
