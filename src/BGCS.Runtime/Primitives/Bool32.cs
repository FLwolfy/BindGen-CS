namespace BGCS.Runtime
{
    using System;

    /// <summary>
    /// 32-bit boolean representation commonly used by native APIs (<c>0</c> = false, non-zero = true).
    /// </summary>
    public struct Bool32 : IEquatable<Bool32>
    {
        /// <summary>
        /// Raw underlying value.
        /// </summary>
        public int value;
        /// <summary>
        /// Initializes from a raw integer value.
        /// </summary>
        /// <param name = "value">Underlying integer value.</param>
        public Bool32(int value)
        {
            this.value = value;
        }

        /// <summary>
        /// Initializes from a managed boolean.
        /// </summary>
        /// <param name = "value">Managed boolean value.</param>
        public Bool32(bool value)
        {
            this.value = value ? 1 : 0;
        }

        /// <inheritdoc/>
        public override readonly bool Equals(object? obj)
        {
            return obj is Bool32 @bool && Equals(@bool);
        }

        /// <summary>
        /// Compares the complete stored representation rather than normalizing nonzero values to true.
        /// </summary>
        /// <param name="other">
        /// The native boolean representation to compare.
        /// </param>
        /// <returns>
        /// True when both stored values are identical; distinct nonzero representations are unequal.
        /// </returns>
        public readonly bool Equals(Bool32 other)
        {
            return this.value == other.value;
        }

        /// <inheritdoc/>
        public override readonly int GetHashCode()
        {
            return this.value;
        }

        /// <summary>
        /// Compares raw native boolean representations without normalization.
        /// </summary>
        /// <param name="left">
        /// The first stored representation.
        /// </param>
        /// <param name="right">
        /// The second stored representation.
        /// </param>
        /// <returns>
        /// True when the raw values are identical; otherwise false.
        /// </returns>
        public static bool operator ==(
            Bool32 left,
            Bool32 right
        ) {
            return left.Equals(right);
        }

        /// <summary>
        /// Compares raw native boolean representations without normalization.
        /// </summary>
        /// <param name="left">
        /// The first stored representation.
        /// </param>
        /// <param name="right">
        /// The second stored representation.
        /// </param>
        /// <returns>
        /// True when the raw values are different; otherwise false.
        /// </returns>
        public static bool operator !=(
            Bool32 left,
            Bool32 right
        ) {
            return !(left == right);
        }

        /// <summary>
        /// Interprets zero as false and every nonzero native representation as true.
        /// </summary>
        /// <param name="b">
        /// The native boolean representation.
        /// </param>
        /// <returns>
        /// False for zero; true for any nonzero stored value.
        /// </returns>
        public static implicit operator bool(Bool32 b)
        {
            return b.value != 0;
        }

        /// <summary>
        /// Returns the complete stored native representation without normalization.
        /// </summary>
        /// <param name="b">
        /// The native boolean representation.
        /// </param>
        /// <returns>
        /// The raw stored value, which may be a noncanonical true representation.
        /// </returns>
        public static implicit operator int(Bool32 b)
        {
            return b.value;
        }

        /// <summary>
        /// Retains a native boolean representation without normalizing it.
        /// </summary>
        /// <param name="b">
        /// The raw value; zero means false and any nonzero value means true.
        /// </param>
        /// <returns>
        /// A wrapper retaining the exact supplied representation.
        /// </returns>
        public static implicit operator Bool32(int b)
        {
            return new(b);
        }

        /// <summary>
        /// Converts a managed boolean into the canonical native representation.
        /// </summary>
        /// <param name="b">
        /// The managed boolean to encode.
        /// </param>
        /// <returns>
        /// A wrapper storing one for true or zero for false.
        /// </returns>
        public static implicit operator Bool32(bool b)
        {
            return new(b);
        }

        /// <summary>
        /// Formats the logical interpretation of the stored native representation.
        /// </summary>
        /// <returns>
        /// The lowercase text false for zero or true for any nonzero representation.
        /// </returns>
        public override readonly string ToString()
        {
            return this.value == 0 ? "false" : "true";
        }
    }
}
