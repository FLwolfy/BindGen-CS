namespace BGCS.CSharp
{
    /// <summary>
    /// Classifies managed primitive carriers used by mutable signature analysis.
    /// </summary>
    public enum CsPrimitiveType
    {
        /// <summary>
        /// No managed primitive classification is available.
        /// </summary>
        Unknown,
        /// <summary>
        /// No returned value.
        /// </summary>
        Void,
        /// <summary>
        /// A managed Boolean value.
        /// </summary>
        Bool,
        /// <summary>
        /// An unsigned 8-bit integer.
        /// </summary>
        Byte,
        /// <summary>
        /// A signed 8-bit integer.
        /// </summary>
        SByte,
        /// <summary>
        /// A UTF-16 code unit.
        /// </summary>
        Char,
        /// <summary>
        /// An unsigned 16-bit integer.
        /// </summary>
        UShort,
        /// <summary>
        /// A signed 16-bit integer.
        /// </summary>
        Short,
        /// <summary>
        /// An unsigned 32-bit integer.
        /// </summary>
        UInt,
        /// <summary>
        /// A signed 32-bit integer.
        /// </summary>
        Int,
        /// <summary>
        /// An unsigned 64-bit integer.
        /// </summary>
        ULong,
        /// <summary>
        /// A signed 64-bit integer.
        /// </summary>
        Long,
        /// <summary>
        /// An IEEE 754 single-precision value.
        /// </summary>
        Float,
        /// <summary>
        /// An IEEE 754 double-precision value.
        /// </summary>
        Double,
    }
}
