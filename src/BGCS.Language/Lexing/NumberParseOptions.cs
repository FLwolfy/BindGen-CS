namespace BGCS.Language.Lexing
{
    using System;

    /// <summary>
    /// Selects optional prefixes, signs, exponents, decimal points, and suffixes accepted by numeric token classification.
    /// </summary>
    [Flags]
    public enum NumberParseOptions
    {
        /// <summary>
        /// Enables no optional numeric syntax.
        /// </summary>
        None = 0,
        /// <summary>
        /// Accepts a hexadecimal prefix.
        /// </summary>
        AllowHex = 1,
        /// <summary>
        /// Accepts a binary prefix.
        /// </summary>
        AllowBinary = 2,
        /// <summary>
        /// Accepts decimal exponent notation.
        /// </summary>
        AllowExponent = 4,
        /// <summary>
        /// Accepts a leading negative sign.
        /// </summary>
        AllowNegative = 8,
        /// <summary>
        /// Accepts a leading positive sign.
        /// </summary>
        AllowPositive = 16,
        /// <summary>
        /// Accepts supported numeric type suffixes.
        /// </summary>
        AllowSuffix = 32,
        /// <summary>
        /// Accepts a fractional decimal point.
        /// </summary>
        AllowDecimal = 64,
        /// <summary>
        /// Accepts every optional numeric syntax declared by this lexer.
        /// </summary>
        AllowAll = AllowHex | AllowBinary | AllowExponent | AllowNegative | AllowPositive | AllowSuffix | AllowDecimal,
    }
}
