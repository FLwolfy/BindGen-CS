namespace BGCS.Emission
{
    using Microsoft.CodeAnalysis.CSharp;

    /// <summary>
    /// Encodes managed text as a C# string literal or its exactly escaped contents.
    /// </summary>
    public static class TextHelper
    {
        /// <summary>
        /// Returns a C# string literal with the given value.
        /// </summary>
        /// <param name = "value">The value that the resulting string literal should have.</param>
        /// <param name = "quote">True to put (double) quotes around the string literal.</param>
        /// <returns>A string literal with the given value.</returns>
        /// <remarks>
        /// Escapes non-printable characters.
        /// </remarks>
        public static string ToLiteral(
            this string value,
            bool quote = false
        ) {
            string literal = SymbolDisplay.FormatLiteral(value, quote: true);
            return quote ? literal : literal[1..^1];
        }
    }
}
