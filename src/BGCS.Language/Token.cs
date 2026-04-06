namespace BGCS.Language
{
    using BGCS;
    using System;

    /// <summary>
    /// Defines the public struct <c>Token</c>.
    /// </summary>
    public struct Token : IEquatable<Token>
    {
        /// <summary>
        /// Exposes public member <c>Type</c>.
        /// </summary>
        public TokenType Type;
        /// <summary>
        /// Exposes public member <c>LiteralType</c>.
        /// </summary>
        public LiteralType LiteralType;
        /// <summary>
        /// Exposes public member <c>NumberType</c>.
        /// </summary>
        public NumberType NumberType;
        /// <summary>
        /// Exposes public member <c>KeywordType</c>.
        /// </summary>
        public KeywordType KeywordType;
        /// <summary>
        /// Exposes public member <c>Start</c>.
        /// </summary>
        public int Start;
        /// <summary>
        /// Exposes public member <c>Length</c>.
        /// </summary>
        public int Length;
        /// <summary>
        /// Exposes public member <c>Source</c>.
        /// </summary>
        public string Source;
        /// <summary>
        /// Exposes public member <c>Location</c>.
        /// </summary>
        public SourceLocation Location;

        /// <summary>
        /// Initializes a new instance of <see cref="Token"/>.
        /// </summary>
        public Token(TokenType type, int start, int length, string source)
        {
            Type = type;
            Start = start;
            Length = length;
            Source = source;
            Location = new();
        }

        /// <summary>
        /// Executes public operation <c>Token</c>.
        /// </summary>
        public Token(TokenType type, int start, int length, string source, SourceLocation location)
        {
            Type = type;
            Start = start;
            Length = length;
            Source = source;
            Location = location;
        }

        /// <summary>
        /// Executes public operation <c>Token</c>.
        /// </summary>
        public Token(int start, int length, string source, SourceLocation location, LiteralType literalType)
        {
            Type = TokenType.Literal;
            Start = start;
            Length = length;
            Source = source;
            Location = location;
            LiteralType = literalType;
        }

        /// <summary>
        /// Executes public operation <c>Token</c>.
        /// </summary>
        public Token(int start, int length, string source, SourceLocation location, NumberType numberType)
        {
            Type = TokenType.Literal;
            Start = start;
            Length = length;
            Source = source;
            Location = location;
            LiteralType = LiteralType.Number;
            NumberType = numberType;
        }

        /// <summary>
        /// Executes public operation <c>Token</c>.
        /// </summary>
        public Token(int start, int length, string source, SourceLocation location, KeywordType keywordType)
        {
            Type = TokenType.Keyword;
            Start = start;
            Length = length;
            Source = source;
            Location = location;
            KeywordType = keywordType;
        }

        /// <summary>
        /// Exposes public member <c>TokenType.Identifier</c>.
        /// </summary>
        public readonly bool IsIdentifier => Type == TokenType.Identifier;

        /// <summary>
        /// Exposes public member <c>TokenType.Keyword</c>.
        /// </summary>
        public readonly bool IsKeyword => Type == TokenType.Keyword;

        /// <summary>
        /// Exposes public member <c>TokenType.Punctuation</c>.
        /// </summary>
        public readonly bool IsPunctuation => Type == TokenType.Punctuation;

        /// <summary>
        /// Exposes public member <c>TokenType.Operator</c>.
        /// </summary>
        public readonly bool IsOperator => Type == TokenType.Operator;

        /// <summary>
        /// Exposes public member <c>TokenType.Literal</c>.
        /// </summary>
        public readonly bool IsLiteral => Type == TokenType.Literal;

        /// <summary>
        /// Exposes public member <c>TokenType.Comment</c>.
        /// </summary>
        public readonly bool IsComment => Type == TokenType.Comment;

        /// <summary>
        /// Executes public operation <c>AsSpan</c>.
        /// </summary>
        public readonly ReadOnlySpan<char> Span => Source.AsSpan(Start, Length);

        /// <summary>
        /// Executes public operation <c>AsString</c>.
        /// </summary>
        public readonly string AsString()
        {
            return Span.ToString();
        }

        /// <summary>
        /// Executes public operation <c>IsString</c>.
        /// </summary>
        public readonly bool IsString(string other)
        {
            if (other.Length != Length) return false;
            for (int i = 0; i < Length; i++)
            {
                if (Span[i] != other[i])
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Executes public operation <c>IsChar</c>.
        /// </summary>
        public readonly bool IsChar(char other)
        {
            if (1 != Length) return false;

            return Span[0] == other;
        }

        /// <summary>
        /// Executes public operation <c>Equals</c>.
        /// </summary>
        public override readonly bool Equals(object? obj)
        {
            return obj is Token token && Equals(token);
        }

        /// <summary>
        /// Executes public operation <c>Equals</c>.
        /// </summary>
        public readonly bool Equals(Token other)
        {
            return Type == other.Type &&
                   Start == other.Start &&
                   Length == other.Length &&
                   Source == other.Source;
        }

        /// <summary>
        /// Returns computed data from <c>GetHashCode</c>.
        /// </summary>
        public override readonly int GetHashCode()
        {
            return HashCode.Combine(Type, Start, Length, Source);
        }

        /// <summary>
        /// Executes public operation <c>ToString</c>.
        /// </summary>
        public override readonly string ToString()
        {
            return $"{Type} \t {(Span[0] == '\n' ? "<newline>" : new(Span))}";
        }

        /// <summary>
        /// Executes public operation <c>Member</c>.
        /// </summary>
        public static bool operator ==(Token left, Token right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Executes public operation <c>Member</c>.
        /// </summary>
        public static bool operator !=(Token left, Token right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Executes public operation <c>Member</c>.
        /// </summary>
        public static bool operator ==(Token left, string right)
        {
            return left.IsString(right);
        }

        /// <summary>
        /// Executes public operation <c>Member</c>.
        /// </summary>
        public static bool operator !=(Token left, string right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Executes public operation <c>Member</c>.
        /// </summary>
        public static bool operator ==(Token left, char right)
        {
            return left.IsChar(right);
        }

        /// <summary>
        /// Executes public operation <c>Member</c>.
        /// </summary>
        public static bool operator !=(Token left, char right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Executes public operation <c>Member</c>.
        /// </summary>
        public static bool operator ==(Token left, KeywordType right)
        {
            return left.KeywordType == right;
        }

        /// <summary>
        /// Executes public operation <c>Member</c>.
        /// </summary>
        public static bool operator !=(Token left, KeywordType right)
        {
            return !(left == right);
        }
    }
}
