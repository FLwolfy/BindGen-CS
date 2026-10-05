namespace BGCS.Core.Text
{
    using System;
    using System.Linq;

    /// <summary>
    /// Removes one delimiter after trimming whitespace at the selected edge, while retaining source-backed spans.
    /// </summary>
    public static class TextExtensions
    {
        /// <summary>
        /// Trims leading whitespace and removes exactly one matching character from the start.
        /// </summary>
        /// <param name="text">
        /// The borrowed source span; its underlying storage is not modified.
        /// </param>
        /// <param name="trim">
        /// The exact character to match after trimming whitespace.
        /// </param>
        /// <param name="result">
        /// The remaining span on success; on failure, the span with leading whitespace removed.
        /// </param>
        /// <returns>
        /// True when the delimiter matches; otherwise false. An empty delimiter sequence matches without consuming characters.
        /// </returns>
        public static bool TryTrimStartFirstOccurrence(
            this ReadOnlySpan<char> text,
            char trim,
            out ReadOnlySpan<char> result
        ) {
            text = text.TrimStart(); // remove leading whitespace
            if (text.Length < 1)
            {
                result = text;
                return false;
            }

            if (text[0] != trim)
            {
                result = text;
                return false;
            }

            result = text[1..];
            return true;
        }

        /// <summary>
        /// Trims trailing whitespace and removes exactly one matching character from the end.
        /// </summary>
        /// <param name="text">
        /// The borrowed source span; its underlying storage is not modified.
        /// </param>
        /// <param name="trim">
        /// The exact character to match after trimming whitespace.
        /// </param>
        /// <param name="result">
        /// The remaining span on success; on failure, the span with trailing whitespace removed.
        /// </param>
        /// <returns>
        /// True when the delimiter matches; otherwise false. An empty delimiter sequence matches without consuming characters.
        /// </returns>
        public static bool TryTrimEndFirstOccurrence(
            this ReadOnlySpan<char> text,
            char trim,
            out ReadOnlySpan<char> result
        ) {
            text = text.TrimEnd(); // remove trailing whitespace
            if (text.Length < 1)
            {
                result = text;
                return false;
            }

            if (text[^1] != trim)
            {
                result = text;
                return false;
            }

            result = text[..^1];
            return true;
        }

        /// <summary>
        /// Trims leading whitespace and removes exactly one matching sequence from the start.
        /// </summary>
        /// <param name="text">
        /// The borrowed source span; its underlying storage is not modified.
        /// </param>
        /// <param name="trim">
        /// The exact sequence to match after trimming whitespace.
        /// </param>
        /// <param name="result">
        /// The remaining span on success; on failure, the span with leading whitespace removed.
        /// </param>
        /// <returns>
        /// True when the delimiter matches; otherwise false. An empty delimiter sequence matches without consuming characters.
        /// </returns>
        public static bool TryTrimStartFirstOccurrence(
            this ReadOnlySpan<char> text,
            ReadOnlySpan<char> trim,
            out ReadOnlySpan<char> result
        ) {
            text = text.TrimStart(); // remove leading whitespace
            if (text.Length < trim.Length)
            {
                result = text;
                return false;
            }

            if (!text[..trim.Length].SequenceEqual(trim))
            {
                result = text;
                return false;
            }

            result = text[trim.Length..];
            return true;
        }

        /// <summary>
        /// Trims trailing whitespace and removes exactly one matching sequence from the end.
        /// </summary>
        /// <param name="text">
        /// The borrowed source span; its underlying storage is not modified.
        /// </param>
        /// <param name="trim">
        /// The exact sequence to match after trimming whitespace.
        /// </param>
        /// <param name="result">
        /// The remaining span on success; on failure, the span with trailing whitespace removed.
        /// </param>
        /// <returns>
        /// True when the delimiter matches; otherwise false. An empty delimiter sequence matches without consuming characters.
        /// </returns>
        public static bool TryTrimEndFirstOccurrence(
            this ReadOnlySpan<char> text,
            ReadOnlySpan<char> trim,
            out ReadOnlySpan<char> result
        ) {
            text = text.TrimEnd(); // remove trailing whitespace
            if (text.Length < trim.Length)
            {
                result = text;
                return false;
            }

            if (!text[^trim.Length..].SequenceEqual(trim))
            {
                result = text;
                return false;
            }

            result = text[..^trim.Length];
            return true;
        }

        /// <summary>
        /// Trims leading whitespace and conditionally removes one matching character from the start.
        /// </summary>
        /// <param name="text">
        /// The borrowed source span; its underlying storage is not modified.
        /// </param>
        /// <param name="trim">
        /// The exact character to remove once.
        /// </param>
        /// <returns>
        /// The source-backed remainder, or the whitespace-trimmed span when no delimiter matches.
        /// </returns>
        public static ReadOnlySpan<char> TrimStartFirstOccurrence(
            this ReadOnlySpan<char> text,
            char trim
        ) {
            text = text.TrimStart(); // remove leading whitespace
            if (text.Length < 1)
            {
                return text;
            }

            if (text[0] != trim)
            {
                return text;
            }

            return text[1..];
        }

        /// <summary>
        /// Trims trailing whitespace and conditionally removes one matching character from the end.
        /// </summary>
        /// <param name="text">
        /// The borrowed source span; its underlying storage is not modified.
        /// </param>
        /// <param name="trim">
        /// The exact character to remove once.
        /// </param>
        /// <returns>
        /// The source-backed remainder, or the whitespace-trimmed span when no delimiter matches.
        /// </returns>
        public static ReadOnlySpan<char> TrimEndFirstOccurrence(
            this ReadOnlySpan<char> text,
            char trim
        ) {
            text = text.TrimEnd(); // remove trailing whitespace
            if (text.Length < 1)
            {
                return text;
            }

            if (text[^1] != trim)
            {
                return text;
            }

            return text[..^1];
        }

        /// <summary>
        /// Trims leading whitespace and conditionally removes one matching sequence from the start.
        /// </summary>
        /// <param name="text">
        /// The borrowed source span; its underlying storage is not modified.
        /// </param>
        /// <param name="trim">
        /// The exact sequence to remove once.
        /// </param>
        /// <returns>
        /// The source-backed remainder, or the whitespace-trimmed span when no delimiter matches.
        /// </returns>
        public static ReadOnlySpan<char> TrimStartFirstOccurrence(
            this ReadOnlySpan<char> text,
            ReadOnlySpan<char> trim
        ) {
            text = text.TrimStart(); // remove leading whitespace
            if (text.Length < trim.Length)
            {
                return text;
            }

            if (!text[..trim.Length].SequenceEqual(trim))
            {
                return text;
            }

            return text[trim.Length..];
        }

        /// <summary>
        /// Trims trailing whitespace and conditionally removes one matching sequence from the end.
        /// </summary>
        /// <param name="text">
        /// The borrowed source span; its underlying storage is not modified.
        /// </param>
        /// <param name="trim">
        /// The exact sequence to remove once.
        /// </param>
        /// <returns>
        /// The source-backed remainder, or the whitespace-trimmed span when no delimiter matches.
        /// </returns>
        public static ReadOnlySpan<char> TrimEndFirstOccurrence(
            this ReadOnlySpan<char> text,
            ReadOnlySpan<char> trim
        ) {
            text = text.TrimEnd(); // remove trailing whitespace
            if (text.Length < trim.Length)
            {
                return text;
            }

            if (!text[^trim.Length..].SequenceEqual(trim))
            {
                return text;
            }

            return text[..^trim.Length];
        }
    }
}
