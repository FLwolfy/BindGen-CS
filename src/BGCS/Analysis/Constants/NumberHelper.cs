using System;
using System.Globalization;
using System.IO;
using System.Numerics;
using BGCS.Core.Text;
using BGCS.Language.Lexing;
using BGCS.Metadata;

namespace BGCS.Analysis.Constants
{
    /// <summary>
    /// Projects numeric macro spellings onto managed literal categories, sharing lexical validation with the language frontend.
    /// </summary>
    public static class NumberHelper
    {
        /// <summary>
        /// Tests whether a nonempty identifier fragment consists exclusively of ASCII decimal digits.
        /// </summary>
        /// <param name="name">The candidate fragment; null and empty fragments are rejected.</param>
        /// <returns>True when every character is an ASCII decimal digit; otherwise false.</returns>
        public static bool IsNumeric(this string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            foreach (char character in name)
            {
                if (!char.IsAsciiDigit(character))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Validates a complete numeric spelling and selects the smallest supported managed integral carrier when no suffix is present.
        /// Explicit suffixes choose a category without checking the target compiler's value range.
        /// </summary>
        /// <param name="name">The native numeric expression; null, empty, and malformed expressions are rejected.</param>
        /// <param name="numberType">The projected category on success, or None when the spelling is invalid.</param>
        /// <param name="options">The optional parentheses, negative sign, hexadecimal prefix, exponent, and suffix syntax accepted.</param>
        /// <returns>True for a valid supported spelling; otherwise false.</returns>
        /// <exception cref="InvalidDataException">An unsuffixed integer exceeds all supported managed integral carriers.</exception>
        public static bool IsNumeric(
            this string name,
            out NumberType numberType,
            NumberParseOptions options = NumberParseOptions.All
        ) {
            if (!TryClassifySyntax(name, options, out ReadOnlySpan<char> spelling, out numberType))
                return false;
            if (numberType != NumberType.Int)
                return true;

            bool negative = spelling[0] == '-';
            if (negative)
                spelling = spelling[1..];
            bool hexadecimal = spelling.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
            if (hexadecimal)
                spelling = spelling[2..];
            if (spelling.EndsWith("L", StringComparison.OrdinalIgnoreCase) || spelling.EndsWith("U", StringComparison.OrdinalIgnoreCase))
                return true;

            // Prefix a zero nibble so hexadecimal parsing preserves an unsigned magnitude.
            BigInteger magnitude = hexadecimal
                ? BigInteger.Parse("0" + spelling.ToString(), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)
                : BigInteger.Parse(spelling, NumberStyles.None, CultureInfo.InvariantCulture);
            BigInteger value = negative ? -magnitude : magnitude;
            numberType = value >= int.MinValue && value <= int.MaxValue ? NumberType.Int
                : value >= uint.MinValue && value <= uint.MaxValue ? NumberType.UInt
                : value >= long.MinValue && value <= long.MaxValue ? NumberType.Long
                : value >= ulong.MinValue && value <= ulong.MaxValue ? NumberType.ULong
                : NumberType.None;
            if (numberType == NumberType.None)
                throw new InvalidDataException($"The number {name} is outside supported managed integral ranges.");
            return true;
        }

        /// <summary>
        /// Validates complete numeric syntax without inferring a value-dependent integral carrier.
        /// </summary>
        /// <param name="name">The candidate spelling; null, empty, and malformed input is rejected.</param>
        /// <param name="options">The optional native numeric syntax accepted.</param>
        /// <returns>True for a syntactically supported numeric expression, including integers beyond managed ranges.</returns>
        public static bool IsNumeric(
            this string name,
            NumberParseOptions options = NumberParseOptions.All
        ) => TryClassifySyntax(name, options, out _, out _);

        private static bool TryClassifySyntax(
            string name,
            NumberParseOptions options,
            out ReadOnlySpan<char> spelling,
            out NumberType type
        ) {
            spelling = name.AsSpan();
            if ((options & NumberParseOptions.AllowBrackets) != 0)
            {
                while (spelling.Length >= 2 && spelling[0] == '(' && spelling[^1] == ')')
                    spelling = spelling[1..^1];
            }
            BGCS.Language.Lexing.NumberParseOptions syntax = BGCS.Language.Lexing.NumberParseOptions.AllowDecimal;
            if ((options & NumberParseOptions.AllowHex) != 0)
                syntax |= BGCS.Language.Lexing.NumberParseOptions.AllowHex;
            if ((options & NumberParseOptions.AllowMinus) != 0)
                syntax |= BGCS.Language.Lexing.NumberParseOptions.AllowNegative;
            if ((options & NumberParseOptions.AllowExponent) != 0)
                syntax |= BGCS.Language.Lexing.NumberParseOptions.AllowExponent;
            if ((options & NumberParseOptions.AllowSuffix) != 0)
                syntax |= BGCS.Language.Lexing.NumberParseOptions.AllowSuffix;
            return Lexer.IsNumber(spelling, out type, syntax);
        }

        /// <summary>
        /// Compares canonical, nonnegative numeric digit strings by length and invariant case-insensitive digit order.
        /// </summary>
        /// <param name="value">
        /// The candidate magnitude without a sign, prefix, or leading zeroes.
        /// </param>
        /// <param name="max">
        /// The canonical upper-bound magnitude in the same radix.
        /// </param>
        /// <returns>
        /// True when the candidate magnitude is strictly smaller; otherwise false.
        /// </returns>
        public static bool NumberStringCompareLess(
            ReadOnlySpan<char> value,
            string max
        ) {
            // Greater
            if (value.Length > max.Length)
                return false;
            // Less
            if (value.Length < max.Length)
                return true;
            for (int i = 0; i < value.Length; i++)
            {
                var c = char.ToLowerInvariant(value[i]);
                var cmp = char.ToLowerInvariant(max[i]);
                // Greater
                if (cmp < c)
                {
                    return false;
                }

                // Less
                if (cmp > c)
                {
                    return true;
                }
            }

            // Equals
            return false;
        }

        /// <summary>
        /// Compares canonical, nonnegative numeric digit strings by length and invariant case-insensitive digit order.
        /// </summary>
        /// <param name="value">
        /// The candidate magnitude without a sign, prefix, or leading zeroes.
        /// </param>
        /// <param name="max">
        /// The canonical upper-bound magnitude in the same radix.
        /// </param>
        /// <returns>
        /// True when the candidate magnitude is smaller or equal; otherwise false.
        /// </returns>
        public static bool NumberStringCompareLessEquals(
            ReadOnlySpan<char> value,
            string max
        ) {
            // Greater
            if (value.Length > max.Length)
                return false;
            // Less
            if (value.Length < max.Length)
                return true;
            for (int i = 0; i < value.Length; i++)
            {
                var c = char.ToLowerInvariant(value[i]);
                var cmp = char.ToLowerInvariant(max[i]);
                // Greater
                if (cmp < c)
                {
                    return false;
                }

                // Less
                if (cmp > c)
                {
                    return true;
                }
            }

            // Equals
            return true;
        }

        /// <summary>
        /// Maps a single recognized numeric category to its C# built-in carrier spelling.
        /// </summary>
        /// <param name="number">
        /// One concrete numeric category; masks and None are invalid.
        /// </param>
        /// <returns>
        /// The C# carrier spelling.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// The category is None, a combined mask, or an unknown value.
        /// </exception>
        public static string GetNumberType(this NumberType number)
        {
            return number switch
            {
                NumberType.None => throw new InvalidOperationException(),
                NumberType.Int => "int",
                NumberType.Double => "double",
                NumberType.Float => "float",
                NumberType.Decimal => "decimal",
                NumberType.UInt => "uint",
                NumberType.Long => "long",
                NumberType.ULong => "ulong",
                _ => throw new InvalidOperationException(),
            };
        }

        /// <summary>
        /// Maps a concrete managed constant category to its built-in carrier spelling.
        /// </summary>
        /// <param name="number">
        /// A numeric or string constant category.
        /// </param>
        /// <returns>
        /// The C# carrier spelling.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// The category has no built-in carrier, including Unknown, Reference, and Custom.
        /// </exception>
        public static string GetCSharpType(this CsConstantType number)
        {
            return number switch
            {
                CsConstantType.Unknown => throw new InvalidOperationException(),
                CsConstantType.Int => "int",
                CsConstantType.Double => "double",
                CsConstantType.Float => "float",
                CsConstantType.Decimal => "decimal",
                CsConstantType.UInt => "uint",
                CsConstantType.Long => "long",
                CsConstantType.ULong => "ulong",
                CsConstantType.String => "string",
                _ => throw new InvalidOperationException(),
            };
        }

        /// <summary>
        /// Maps a numeric literal category onto its corresponding constant projection category.
        /// </summary>
        /// <param name="type">
        /// The numeric category produced by literal classification.
        /// </param>
        /// <returns>
        /// The corresponding constant category, or Unknown for None, masks, and unrecognized categories.
        /// </returns>
        public static CsConstantType GetConstantType(this NumberType type)
        {
            return type switch
            {
                NumberType.Int => CsConstantType.Int,
                NumberType.Double => CsConstantType.Double,
                NumberType.Float => CsConstantType.Float,
                NumberType.Decimal => CsConstantType.Decimal,
                NumberType.UInt => CsConstantType.UInt,
                NumberType.Long => CsConstantType.Long,
                NumberType.ULong => CsConstantType.ULong,
                _ => CsConstantType.Unknown,
            };
        }
    }
}
