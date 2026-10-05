namespace BGCS.Metadata
{
    /// <summary>
    /// Selects a managed literal carrier or an explicitly mapped representation for a native constant projection.
    /// </summary>
    public enum CsConstantType
    {
        /// <summary>
        /// A constant whose managed value type has not been classified.
        /// </summary>
        Unknown,
        /// <summary>
        /// A constant expressed through another symbol.
        /// </summary>
        Reference,
        /// <summary>
        /// A managed string literal.
        /// </summary>
        String,
        /// <summary>
        /// A signed 32-bit integer literal.
        /// </summary>
        Int,
        /// <summary>
        /// A double-precision numeric literal.
        /// </summary>
        Double,
        /// <summary>
        /// A single-precision numeric literal.
        /// </summary>
        Float,
        /// <summary>
        /// A managed decimal literal.
        /// </summary>
        Decimal,
        /// <summary>
        /// An unsigned 32-bit integer literal.
        /// </summary>
        UInt,
        /// <summary>
        /// A signed 64-bit integer literal.
        /// </summary>
        Long,
        /// <summary>
        /// An unsigned 64-bit integer literal.
        /// </summary>
        ULong,
        /// <summary>
        /// An explicitly mapped constant representation.
        /// </summary>
        Custom,
    }
}
