namespace BGCS.Core.Text
{
    /// <summary>
    /// Identifies managed numeric literal carriers and masks grouping integral or fractional categories.
    /// </summary>
    public enum NumberType : byte
    {

        /// <summary>
        /// No recognized numeric literal category.
        /// </summary>
        None = 0,
        /// <summary>
        /// A signed 32-bit integral carrier.
        /// </summary>
        Int = 1,
        /// <summary>
        /// A double-precision floating-point carrier.
        /// </summary>
        Double = 2,
        /// <summary>
        /// A single-precision floating-point carrier.
        /// </summary>
        Float = 4,
        /// <summary>
        /// A managed decimal carrier.
        /// </summary>
        Decimal = 8,
        /// <summary>
        /// An unsigned 32-bit integral carrier.
        /// </summary>
        UInt = 16,
        /// <summary>
        /// A signed 64-bit integral carrier.
        /// </summary>
        Long = 32,
        /// <summary>
        /// An unsigned 64-bit integral carrier.
        /// </summary>
        ULong = 64,

        /// <summary>
        /// Mask selecting all supported integral literal categories.
        /// </summary>
        AnyInt = Int | UInt | Long | ULong,

        /// <summary>
        /// Mask selecting all supported fractional literal categories.
        /// </summary>
        AnyFloat = Float | Double | Decimal,
    }
}
