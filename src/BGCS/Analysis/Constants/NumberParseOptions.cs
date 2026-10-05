namespace BGCS.Analysis.Constants
{
    using System;

    /// <summary>
    /// Controls optional native macro spelling accepted before projection onto managed numeric carriers.
    /// </summary>
    [Flags]
    public enum NumberParseOptions
    {
        /// <summary>
        /// Enables no optional native numeric syntax.
        /// </summary>
        None = 0,
        /// <summary>
        /// Accepts a parenthesized numeric expression.
        /// </summary>
        AllowBrackets = 1,
        /// <summary>
        /// Accepts a hexadecimal prefix.
        /// </summary>
        AllowHex = 2,
        /// <summary>
        /// Accepts a leading negative sign.
        /// </summary>
        AllowMinus = 4,
        /// <summary>
        /// Accepts exponent notation.
        /// </summary>
        AllowExponent = 8,
        /// <summary>
        /// Accepts supported C numeric type suffixes.
        /// </summary>
        AllowSuffix = 16,
        /// <summary>
        /// Accepts every optional native numeric syntax declared by this classifier.
        /// </summary>
        All = AllowBrackets | AllowHex | AllowMinus | AllowExponent | AllowSuffix,
    }
}
