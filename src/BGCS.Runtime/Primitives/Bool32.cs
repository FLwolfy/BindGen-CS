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
        public int Value;

        /// <summary>
        /// Initializes from a raw integer value.
        /// </summary>
        /// <param name="value">Underlying integer value.</param>
        public Bool32(int value)
        {
            Value = value;
        }

        /// <summary>
        /// Initializes from a managed boolean.
        /// </summary>
        /// <param name="value">Managed boolean value.</param>
        public Bool32(bool value)
        {
            Value = value ? 1 : 0;
        }

        /// <inheritdoc/>
        public override readonly bool Equals(object? obj)
        {
            return obj is Bool32 @bool && Equals(@bool);
        }

        /// <summary>
        /// Compares this instance to another <see cref="Bool32"/> by raw value.
        /// </summary>
        public readonly bool Equals(Bool32 other)
        {
            return Value == other.Value;
        }

        /// <inheritdoc/>
        public override readonly int GetHashCode()
        {
            return Value;
        }

        /// <summary>
        /// Compares two <see cref="Bool32"/> values.
        /// </summary>
        public static bool operator ==(Bool32 left, Bool32 right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Compares two <see cref="Bool32"/> values for inequality.
        /// </summary>
        public static bool operator !=(Bool32 left, Bool32 right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Converts <see cref="Bool32"/> to managed <see cref="bool"/>.
        /// </summary>
        public static implicit operator bool(Bool32 b)
        {
            return b.Value != 0;
        }

        /// <summary>
        /// Converts <see cref="Bool32"/> to its raw integer value.
        /// </summary>
        public static implicit operator int(Bool32 b)
        {
            return b.Value;
        }

        /// <summary>
        /// Converts a raw integer to <see cref="Bool32"/>.
        /// </summary>
        public static implicit operator Bool32(int b)
        {
            return new(b);
        }

        /// <summary>
        /// Converts a managed boolean to <see cref="Bool32"/>.
        /// </summary>
        public static implicit operator Bool32(bool b)
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
