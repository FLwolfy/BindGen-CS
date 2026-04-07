namespace BGCS.Runtime
{
    using System;

    /// <summary>
    /// 8-bit boolean representation commonly used by native APIs (<c>0</c> = false, non-zero = true).
    /// </summary>
    public struct Bool8 : IEquatable<Bool8>
    {
        /// <summary>
        /// Raw underlying value.
        /// </summary>
        public byte Value;

        /// <summary>
        /// Initializes from a raw byte.
        /// </summary>
        /// <param name="value">Underlying byte value.</param>
        public Bool8(byte value)
        {
            Value = value;
        }

        /// <summary>
        /// Initializes from a managed boolean.
        /// </summary>
        /// <param name="value">Managed boolean value.</param>
        public Bool8(bool value)
        {
            Value = value ? (byte)1 : (byte)0;
        }

        /// <inheritdoc/>
        public override readonly bool Equals(object? obj)
        {
            return obj is Bool8 @bool && Equals(@bool);
        }

        /// <summary>
        /// Compares this instance to another <see cref="Bool8"/> by raw value.
        /// </summary>
        public readonly bool Equals(Bool8 other)
        {
            return Value == other.Value;
        }

        /// <inheritdoc/>
        public override readonly int GetHashCode()
        {
            return Value;
        }

        /// <summary>
        /// Compares two <see cref="Bool8"/> values.
        /// </summary>
        public static bool operator ==(Bool8 left, Bool8 right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Compares two <see cref="Bool8"/> values for inequality.
        /// </summary>
        public static bool operator !=(Bool8 left, Bool8 right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Converts <see cref="Bool8"/> to managed <see cref="bool"/>.
        /// </summary>
        public static implicit operator bool(Bool8 b)
        {
            return b.Value != 0;
        }

        /// <summary>
        /// Converts <see cref="Bool8"/> to its raw byte value.
        /// </summary>
        public static implicit operator byte(Bool8 b)
        {
            return b.Value;
        }

        /// <summary>
        /// Converts a raw byte value to <see cref="Bool8"/>.
        /// </summary>
        public static implicit operator Bool8(byte b)
        {
            return new(b);
        }

        /// <summary>
        /// Converts a managed boolean to <see cref="Bool8"/>.
        /// </summary>
        public static implicit operator Bool8(bool b)
        {
            return new(b);
        }

        /// <summary>
        /// Returns a managed boolean string representation.
        /// </summary>
        public override readonly string ToString()
        {
            return Value == 0 ? "false" : "true";
        }
    }
}
