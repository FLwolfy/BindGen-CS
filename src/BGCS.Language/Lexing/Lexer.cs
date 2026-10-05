using System.Collections.Generic;
using BGCS.Core.Text;
using BGCS.Language.Diagnostics;

namespace BGCS.Language.Lexing
{
    using System;
    using System.Linq;
    using System.Runtime.CompilerServices;

    /// <summary>
    /// Splits authoring text into source-backed identifiers, keywords, literals, operators, punctuation, and comments.
    /// </summary>
    public class Lexer
    {

        /// <summary>
        /// Operators recognized by this lexer, ordered from longest to shortest.
        /// </summary>
        protected readonly List<string> operators = new();

        /// <summary>
        /// Single-character punctuation delimiters recognized by this lexer.
        /// </summary>
        protected readonly List<char> punctuations = new();

        /// <summary>
        /// Keyword spellings used to recognize reserved tokens.
        /// </summary>
        protected readonly List<string> keywords;

        /// <summary>
        /// Maps exact keyword spellings to their semantic token categories.
        /// </summary>
        protected readonly Dictionary<string, KeywordType> keywordMap = new();

        /// <summary>
        /// Exponent spellings that must remain inside numeric tokens rather than become operators.
        /// </summary>
        protected readonly List<string> numberNotations = new();
        /// <summary>
        /// Creates the default keyword, punctuation, operator, and numeric-notation tables.
        /// </summary>
        public Lexer()
        {
            operators.Add("+");
            operators.Add("-");
            operators.Add("*");
            operators.Add("/");
            operators.Add("%");
            operators.Add("<<");
            operators.Add(">>");
            operators.Add("|");
            operators.Add("&");
            operators.Add("^");
            operators.Add("~");
            operators.Add("!");
            operators.Add("=");
            operators.Add("++");
            operators.Add("--");
            operators.Add("&&");
            operators.Add("||");
            operators = operators.OrderByDescending(x => x.Length).ToList();
            numberNotations.Add("e+");
            numberNotations.Add("e-");
            punctuations.Add('(');
            punctuations.Add(')');
            punctuations.Add('{');
            punctuations.Add('}');
            punctuations.Add('[');
            punctuations.Add(']');
            punctuations.Add('<');
            punctuations.Add('>');
            punctuations.Add(';');
            punctuations.Add(',');
            keywords = new(Enum.GetNames<KeywordType>().Skip(1).Select(x => x.ToLowerInvariant()));
            for (int i = 0; i < keywords.Count; i++)
            {
                var keyword = keywords[i];
                keywordMap.Add(keyword, Enum.Parse<KeywordType>(keyword, true));
            }
        }

        private static bool StartsWith(
            ReadOnlySpan<char> text,
            string value
        ) {
            if (text.Length < value.Length)
                return false;
            for (int i = 0; i < value.Length; i++)
            {
                if (text[i] != value[i])
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Builds a token stream retaining source text and reports unterminated comments or literals as diagnostics.
        /// </summary>
        /// <param name="input">
        /// The complete input text; an empty or null value produces an empty token stream.
        /// </param>
        /// <param name="filename">
        /// The source name copied into token and error locations.
        /// </param>
        /// <returns>
        /// The tokens and diagnostics; tokens are null when lexing cannot complete.
        /// </returns>
        public virtual LexerResult Tokenize(
            string input,
            string filename
        ) {
            DiagnosticBag diagnostics = new();
            List<Token> tokens = new();
            if (string.IsNullOrEmpty(input))
                return new(diagnostics, tokens);

            int position = 0;
            int line = 0;
            int column = 0;
            while (position < input.Length)
            {
                if (IsWhiteSpaceOrNewLine(input, position, out int separatorLength, out _))
                {
                    Advance(separatorLength);
                    continue;
                }

                int start = position;
                SourceLocation location = new(filename, start, line, column);
                ReadOnlySpan<char> remaining = input.AsSpan(position);
                if (remaining.StartsWith("//", StringComparison.Ordinal))
                {
                    Advance(2);
                    int contentStart = position;
                    SourceLocation contentLocation = new(filename, contentStart, line, column);
                    while (position < input.Length && !IsNewLine(input, position))
                        Advance(1);
                    tokens.Add(new(TokenType.Comment, contentStart, position - contentStart, input, contentLocation));
                    continue;
                }

                if (remaining.StartsWith("/*", StringComparison.Ordinal))
                {
                    Advance(2);
                    int contentStart = position;
                    SourceLocation contentLocation = new(filename, contentStart, line, column);
                    while (position < input.Length && !input.AsSpan(position).StartsWith("*/", StringComparison.Ordinal))
                        Advance(1);
                    if (position == input.Length)
                        return Fail("Unterminated block comment.", location);
                    tokens.Add(new(TokenType.Comment, contentStart, position - contentStart, input, contentLocation));
                    Advance(2);
                    continue;
                }

                char character = input[position];
                if (character is '"' or '\'')
                {
                    char delimiter = character;
                    Advance(1);
                    int contentStart = position;
                    SourceLocation contentLocation = new(filename, contentStart, line, column);
                    while (position < input.Length && input[position] != delimiter)
                    {
                        if (input[position] == '\\')
                        {
                            Advance(1);
                            if (position == input.Length)
                                return Fail("Unterminated quoted literal.", location);
                        }
                        Advance(1);
                    }
                    if (position == input.Length)
                        return Fail("Unterminated quoted literal.", location);
                    int contentLength = position - contentStart;
                    if (delimiter == '\'' && !IsCharacterLiteral(input.AsSpan(contentStart, contentLength)))
                        return Fail("A character literal must contain one character or one escape sequence.", location);
                    tokens.Add(new(contentStart, contentLength, input, contentLocation,
                        delimiter == '"' ? LiteralType.String : LiteralType.Char));
                    Advance(1);
                    continue;
                }

                if (IsOperator(input, position, out int operatorLength))
                {
                    tokens.Add(new(TokenType.Operator, position, operatorLength, input, location));
                    Advance(operatorLength);
                    continue;
                }
                if (IsPunctuation(input, position, out int punctuationLength))
                {
                    tokens.Add(new(TokenType.Punctuation, position, punctuationLength, input, location));
                    Advance(punctuationLength);
                    continue;
                }

                do
                {
                    Advance(1);
                }
                while (position < input.Length
                    && input[position] is not ('"' or '\'')
                    && !IsWhiteSpaceOrNewLine(input, position, out _, out _)
                    && !IsOperator(input, position, out _)
                    && !IsPunctuation(input, position, out _));
                ReadOnlySpan<char> spelling = input.AsSpan(start, position - start);
                if (IsKeyword(spelling, out KeywordType keyword))
                    tokens.Add(new(start, spelling.Length, input, location, keyword));
                else if (IsNumber(spelling, out NumberType number))
                    tokens.Add(new(start, spelling.Length, input, location, number));
                else
                    tokens.Add(new(TokenType.Identifier, start, spelling.Length, input, location));
            }
            return new(diagnostics, tokens);

            void Advance(int count)
            {
                int limit = position + count;
                while (position < limit)
                {
                    char consumed = input[position++];
                    if (consumed == '\r' || consumed == '\n' && (position == 1 || input[position - 2] != '\r'))
                    {
                        line++;
                        column = 0;
                    }
                    else if (consumed != '\n')
                        column++;
                }
            }

            LexerResult Fail(
                string message,
                SourceLocation location
            ) {
                diagnostics.Error(message, location);
                return new(diagnostics, null);
            }
        }

        private static bool IsCharacterLiteral(ReadOnlySpan<char> text)
        {
            if (text.Length == 1)
                return text[0] is not ('\\' or '\r' or '\n');
            if (text.Length < 2 || text[0] != '\\')
                return false;
            if (text.Length == 2)
                return "'\"\\0abfnrtv".Contains(text[1]);
            int digits = text.Length - 2;
            if (!(text[1] == 'u' && digits == 4 || text[1] == 'U' && digits == 8 || text[1] == 'x' && digits is >= 1 and <= 4))
                return false;
            foreach (char digit in text[2..])
            {
                if (!char.IsAsciiHexDigit(digit))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Adjusts a source interval to exclude leading and trailing ASCII spaces without copying its text.
        /// </summary>
        /// <param name="input">
        /// The source span containing the interval.
        /// </param>
        /// <param name="start">
        /// The inclusive interval start, updated in place.
        /// </param>
        /// <param name="length">
        /// The exclusive interval end, updated in place; this parameter is an end position rather than a count.
        /// </param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Trim(
            ReadOnlySpan<char> input,
            ref int start,
            ref int length
        ) {
            bool wasChar = false;
            for (int i = start; i < length; i++)
            {
                var c = input[i];
                if (c == ' ' && !wasChar)
                {
                    start++;
                }
                else
                {
                    wasChar = true;
                }
            }

            wasChar = false;
            for (int i = length - 1; i >= start; i--)
            {
                var c = input[i];
                if (c == ' ' && !wasChar)
                {
                    length--;
                }
                else
                {
                    wasChar = true;
                }
            }
        }

        /// <summary>
        /// Recognizes an ASCII space, tab, LF, CR, or CRLF at a source position.
        /// </summary>
        /// <param name="input">
        /// The source span to inspect.
        /// </param>
        /// <param name="index">
        /// An existing source character position.
        /// </param>
        /// <param name="length">
        /// The number of consumed characters, or zero when no separator matches.
        /// </param>
        /// <param name="isNewLine">
        /// True for LF, CR, or CRLF; false for whitespace or no match.
        /// </param>
        /// <returns>
        /// True when one of the supported separators starts at the position; otherwise false.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsWhiteSpaceOrNewLine(
            ReadOnlySpan<char> input,
            int index,
            out int length,
            out bool isNewLine
        ) {
            length = 0;
            if (input[index] == ' ')
            {
                length = 1;
                isNewLine = false;
                return true;
            }

            if (input[index] is '\r' or '\n')
            {
                length = input[index] == '\r' && index + 1 < input.Length && input[index + 1] == '\n' ? 2 : 1;
                isNewLine = true;
                return true;
            }

            if (input[index] == '\t')
            {
                length = 1;
                isNewLine = false;
                return true;
            }

            isNewLine = false;
            return false;
        }

        /// <summary>
        /// Checks whether an LF, CR, or CRLF begins at an existing source position.
        /// </summary>
        /// <param name="input">
        /// The source span to inspect.
        /// </param>
        /// <param name="index">
        /// An existing source character position.
        /// </param>
        /// <returns>
        /// True when the position starts a supported line ending; otherwise false.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNewLine(
            ReadOnlySpan<char> input,
            int index
        ) {
            return input[index] is '\r' or '\n';
        }

        /// <summary>
        /// Checks whether a position begins LF, CR, CRLF, or lies exactly at the end of a source span.
        /// </summary>
        /// <param name="input">
        /// The source span to inspect.
        /// </param>
        /// <param name="index">
        /// An existing character position or the source length.
        /// </param>
        /// <param name="length">
        /// The consumed line-ending length, or zero at EOF or when no match exists.
        /// </param>
        /// <returns>
        /// True for a supported line ending or EOF; otherwise false.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNewLineOrEof(
            ReadOnlySpan<char> input,
            int index,
            out int length
        ) {
            length = 0;
            if (index == input.Length)
                return true;
            if (input[index] is '\r' or '\n')
            {
                length = input[index] == '\r' && index + 1 < input.Length && input[index + 1] == '\n' ? 2 : 1;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Checks whether a source character belongs to this lexer's configured punctuation table.
        /// </summary>
        /// <param name="input">
        /// The source span to inspect.
        /// </param>
        /// <param name="index">
        /// An existing source character position.
        /// </param>
        /// <param name="length">
        /// One when punctuation matches, or zero otherwise.
        /// </param>
        /// <returns>
        /// True when the character is configured punctuation; otherwise false.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsPunctuation(
            ReadOnlySpan<char> input,
            int index,
            out int length
        ) {
            length = 0;
            for (int i = 0; i < punctuations.Count; i++)
            {
                var separator = punctuations[i];
                if (input[index] == separator)
                {
                    length = 1;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Finds the first configured keyword spelling that prefixes text at a source position.
        /// </summary>
        /// <param name="input">
        /// The source span to inspect.
        /// </param>
        /// <param name="index">
        /// The position from which keyword matching begins.
        /// </param>
        /// <param name="length">
        /// The matching spelling length, or zero when no spelling matches.
        /// </param>
        /// <returns>
        /// True when a configured keyword prefixes the remaining source; token-boundary validation belongs to the caller.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsKeyword(
            ReadOnlySpan<char> input,
            int index,
            out int length
        ) {
            length = 0;
            var span = input[index..];
            for (int i = 0; i < keywords.Count; i++)
            {
                var keyword = keywords[i];
                if (StartsWith(span, keyword))
                {
                    length = keyword.Length;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Classifies a complete spelling using exact, case-sensitive keyword equality.
        /// </summary>
        /// <param name="span">
        /// The complete token spelling.
        /// </param>
        /// <param name="keywordType">
        /// The matching keyword category, or Unknown when no keyword matches.
        /// </param>
        /// <returns>
        /// True when the complete spelling is a configured keyword; otherwise false.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsKeyword(
            ReadOnlySpan<char> span,
            out KeywordType keywordType
        ) {
            keywordType = KeywordType.Unknown;
            for (int i = 0; i < keywords.Count; i++)
            {
                var keyword = keywords[i];
                if (span.SequenceEqual(keyword))
                {
                    keywordType = keywordMap[keyword];
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Matches configured operators while preserving configured signed exponent notation inside tokens.
        /// </summary>
        /// <param name="input">
        /// The source span to inspect.
        /// </param>
        /// <param name="index">
        /// The position from which operator matching begins.
        /// </param>
        /// <param name="length">
        /// The matching operator length, or zero when no operator matches.
        /// </param>
        /// <returns>
        /// True when a configured operator begins at the position and is not excluded as numeric notation.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsOperator(
            ReadOnlySpan<char> input,
            int index,
            out int length
        ) {
            length = 0;
            var span = input[index..];
            if (index > 1 && input[index] is '+' or '-')
            {
                foreach (string notation in numberNotations)
                {
                    if (!input[(index - 1)..].StartsWith(notation, StringComparison.OrdinalIgnoreCase))
                        continue;
                    int numberStart = index - 1;
                    while (numberStart > 0 && (char.IsAsciiDigit(input[numberStart - 1]) || input[numberStart - 1] == '.'))
                        numberStart--;
                    if ((numberStart == 0 || !char.IsLetterOrDigit(input[numberStart - 1]) && input[numberStart - 1] != '_')
                        && IsNumber(input[numberStart..(index - 1)], out _, NumberParseOptions.AllowDecimal))
                        return false;
                }
            }

            for (int i = 0; i < operators.Count; i++)
            {
                var op = operators[i];
                if (StartsWith(span, op))
                {
                    length = op.Length;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Classifies numeric token spelling according to the selected signs, prefixes, exponent, and suffix policy.
        /// </summary>
        /// <param name="input">
        /// The complete candidate spelling.
        /// </param>
        /// <param name="type">
        /// The recognized numeric category, or None when classification fails.
        /// </param>
        /// <param name="options">
        /// The optional numeric syntax allowed for this candidate.
        /// </param>
        /// <returns>
        /// True when the entire spelling satisfies numeric classification; otherwise false.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNumber(
            ReadOnlySpan<char> input,
            out NumberType type,
            NumberParseOptions options = NumberParseOptions.AllowAll
        ) {
            type = NumberType.None;
            if (input.IsEmpty)
                return false;
            int position = 0;
            if (input[0] is '+' or '-')
            {
                NumberParseOptions sign = input[0] == '+' ? NumberParseOptions.AllowPositive : NumberParseOptions.AllowNegative;
                if ((options & sign) == 0 || input.Length == 1)
                    return false;
                position++;
            }

            int radix = 10;
            if (input[position..].StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if ((options & NumberParseOptions.AllowHex) == 0)
                    return false;
                radix = 16;
                position += 2;
            }
            else if (input[position..].StartsWith("0b", StringComparison.OrdinalIgnoreCase))
            {
                if ((options & NumberParseOptions.AllowBinary) == 0)
                    return false;
                radix = 2;
                position += 2;
            }

            int digits = ConsumeDigits(input, ref position, radix);
            bool fractional = false;
            if (radix == 10 && position < input.Length && input[position] == '.')
            {
                if ((options & NumberParseOptions.AllowDecimal) == 0)
                    return false;
                position++;
                digits += ConsumeDigits(input, ref position, 10);
                fractional = true;
            }
            if (digits == 0)
                return false;
            if (radix == 10 && position < input.Length && input[position] is 'e' or 'E')
            {
                if ((options & NumberParseOptions.AllowExponent) == 0)
                    return false;
                position++;
                if (position < input.Length && input[position] is '+' or '-')
                    position++;
                if (ConsumeDigits(input, ref position, 10) == 0)
                    return false;
                fractional = true;
            }

            NumberType candidate = fractional ? NumberType.Double : NumberType.Int;
            ReadOnlySpan<char> suffix = input[position..];
            if (!suffix.IsEmpty)
            {
                if ((options & NumberParseOptions.AllowSuffix) == 0)
                    return false;
                if (!fractional && suffix.Equals("L", StringComparison.OrdinalIgnoreCase))
                    candidate = NumberType.Long;
                else if (!fractional && suffix.Equals("U", StringComparison.OrdinalIgnoreCase))
                    candidate = NumberType.UInt;
                else if (!fractional && (suffix.Equals("UL", StringComparison.OrdinalIgnoreCase) || suffix.Equals("LU", StringComparison.OrdinalIgnoreCase)))
                    candidate = NumberType.ULong;
                else if (radix == 10 && suffix.Equals("F", StringComparison.OrdinalIgnoreCase))
                    candidate = NumberType.Float;
                else if (radix == 10 && suffix.Equals("D", StringComparison.OrdinalIgnoreCase))
                    candidate = NumberType.Double;
                else if (radix == 10 && suffix.Equals("M", StringComparison.OrdinalIgnoreCase))
                    candidate = NumberType.Decimal;
                else
                    return false;
            }
            type = candidate;
            return true;

            static int ConsumeDigits(
                ReadOnlySpan<char> input,
                ref int position,
                int digitRadix
            ) {
                int start = position;
                while (position < input.Length)
                {
                    char character = input[position];
                    bool valid = digitRadix switch
                    {
                        16 => char.IsAsciiHexDigit(character),
                        2 => character is '0' or '1',
                        _ => char.IsAsciiDigit(character)
                    };
                    if (!valid)
                        break;
                    position++;
                }
                return position - start;
            }
        }
    }
}
