using System;
using BGCS.Core.Text;
using BGCS.Language.Diagnostics;

namespace BGCS.Language.Lexing;

/// <summary>
/// Identifies a UTF-16 range in retained source text, with lexical category and optional literal or keyword metadata.
/// Equality compares category, range and source text, excluding location and specialized metadata.
/// </summary>
public struct Token : IEquatable<Token>
{
    /// <summary>
    /// The broad lexical category of this range.
    /// </summary>
    public TokenType type;
    /// <summary>
    /// The literal category, meaningful when the token category is Literal.
    /// </summary>
    public LiteralType literalType;
    /// <summary>
    /// The numeric representation, meaningful for numeric literals.
    /// </summary>
    public NumberType numberType;
    /// <summary>
    /// The keyword classification, meaningful when the token category is Keyword.
    /// </summary>
    public KeywordType keywordType;
    /// <summary>
    /// The zero-based UTF-16 start offset into the retained source.
    /// </summary>
    public int start;
    /// <summary>
    /// The number of UTF-16 code units in the token range.
    /// </summary>
    public int length;
    /// <summary>
    /// The complete source text retained by this token.
    /// </summary>
    public string source;
    /// <summary>
    /// The source file and position used for diagnostics.
    /// </summary>
    public SourceLocation location;

    /// <summary>
    /// Creates a categorized source range without diagnostic position metadata.
    /// </summary>
    /// <param name="type">The broad lexical category.</param>
    /// <param name="start">The zero-based UTF-16 start offset.</param>
    /// <param name="length">The number of UTF-16 code units in the range.</param>
    /// <param name="source">The complete source text retained by the token.</param>
    public Token(
        TokenType type,
        int start,
        int length,
        string source
    ) : this(type, start, length, source, default) { }

    /// <summary>
    /// Creates a categorized source range with diagnostic position metadata.
    /// Range validation occurs when the span is accessed.
    /// </summary>
    /// <param name="type">The broad lexical category.</param>
    /// <param name="start">The zero-based UTF-16 start offset.</param>
    /// <param name="length">The number of UTF-16 code units in the range.</param>
    /// <param name="source">The complete source text retained by the token.</param>
    /// <param name="location">The diagnostic position supplied by the lexer.</param>
    public Token(
        TokenType type,
        int start,
        int length,
        string source,
        SourceLocation location
    ) {
        this.type = type;
        this.start = start;
        this.length = length;
        this.source = source;
        this.location = location;
    }

    /// <summary>
    /// Creates a literal token with the supplied literal classification.
    /// </summary>
    /// <param name="start">The zero-based UTF-16 start offset.</param>
    /// <param name="length">The number of UTF-16 code units in the range.</param>
    /// <param name="source">The complete source text retained by the token.</param>
    /// <param name="location">The diagnostic position supplied by the lexer.</param>
    /// <param name="literalType">The literal classification, such as string or number.</param>
    public Token(
        int start,
        int length,
        string source,
        SourceLocation location,
        LiteralType literalType
    ) : this(TokenType.Literal, start, length, source, location)
    {
        this.literalType = literalType;
    }

    /// <summary>
    /// Creates a numeric literal token with its representation classification.
    /// </summary>
    /// <param name="start">The zero-based UTF-16 start offset.</param>
    /// <param name="length">The number of UTF-16 code units in the range.</param>
    /// <param name="source">The complete source text retained by the token.</param>
    /// <param name="location">The diagnostic position supplied by the lexer.</param>
    /// <param name="numberType">The numeric representation classification.</param>
    public Token(
        int start,
        int length,
        string source,
        SourceLocation location,
        NumberType numberType
    ) : this(start, length, source, location, LiteralType.Number)
    {
        this.numberType = numberType;
    }

    /// <summary>
    /// Creates a keyword token with its semantic keyword classification.
    /// </summary>
    /// <param name="start">The zero-based UTF-16 start offset.</param>
    /// <param name="length">The number of UTF-16 code units in the range.</param>
    /// <param name="source">The complete source text retained by the token.</param>
    /// <param name="location">The diagnostic position supplied by the lexer.</param>
    /// <param name="keywordType">The recognized keyword classification.</param>
    public Token(
        int start,
        int length,
        string source,
        SourceLocation location,
        KeywordType keywordType
    ) : this(TokenType.Keyword, start, length, source, location)
    {
        this.keywordType = keywordType;
    }

    /// <summary>
    /// Whether this range represents a declaration or reference identifier.
    /// </summary>
    public readonly bool isIdentifier => type == TokenType.Identifier;
    /// <summary>
    /// Whether this range represents a recognized keyword.
    /// </summary>
    public readonly bool isKeyword => type == TokenType.Keyword;
    /// <summary>
    /// Whether this range represents a syntax delimiter.
    /// </summary>
    public readonly bool isPunctuation => type == TokenType.Punctuation;
    /// <summary>
    /// Whether this range represents an expression operator.
    /// </summary>
    public readonly bool isOperator => type == TokenType.Operator;
    /// <summary>
    /// Whether this range represents a literal value.
    /// </summary>
    public readonly bool isLiteral => type == TokenType.Literal;
    /// <summary>
    /// Whether this range represents a source comment.
    /// </summary>
    public readonly bool isComment => type == TokenType.Comment;
    /// <summary>
    /// A non-allocating view of the token's retained source range.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The mutable range is outside its source text.</exception>
    public readonly ReadOnlySpan<char> span => source.AsSpan(start, length);

    /// <summary>
    /// Copies the token's source range into an independent string.
    /// </summary>
    /// <returns>The exact token text, including an empty string for an empty range.</returns>
    public readonly string AsString() => span.ToString();

    /// <summary>
    /// Compares the token text to a string using ordinal UTF-16 equality.
    /// </summary>
    /// <param name="other">The text to compare with the token range.</param>
    /// <returns>True when lengths and every character match; otherwise false.</returns>
    /// <exception cref="ArgumentNullException">The comparison string is null.</exception>
    public readonly bool IsString(string other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return span.SequenceEqual(other.AsSpan());
    }

    /// <summary>
    /// Tests whether the token consists of one specified UTF-16 character.
    /// </summary>
    /// <param name="other">The character to compare.</param>
    /// <returns>True for a one-character matching range; otherwise false.</returns>
    public readonly bool IsChar(char other) => length == 1 && span[0] == other;

    /// <inheritdoc />
    public override readonly bool Equals(object? obj) => obj is Token other && Equals(other);

    /// <summary>
    /// Compares category, source text and range; specialized metadata and diagnostic position are excluded.
    /// </summary>
    /// <param name="other">The token to compare.</param>
    /// <returns>True when both tokens identify the same category, source text and range.</returns>
    public readonly bool Equals(Token other)
        => type == other.type && start == other.start && length == other.length && source == other.source;

    /// <inheritdoc />
    public override readonly int GetHashCode() => HashCode.Combine(type, start, length, source);

    /// <summary>
    /// Formats the lexical category and token text, displaying a newline as a diagnostic marker.
    /// </summary>
    /// <returns>The category and text; an empty range is formatted without indexing its first character.</returns>
    public override readonly string ToString()
    {
        ReadOnlySpan<char> text = span;
        string value = !text.IsEmpty && text[0] == '\n' ? "<newline>" : text.ToString();
        return $"{type} \t {value}";
    }

    /// <summary>
    /// Compares two tokens by category, source text and range.
    /// </summary>
    /// <param name="left">The first token.</param>
    /// <param name="right">The second token.</param>
    /// <returns>True when token equality succeeds.</returns>
    public static bool operator ==(
        Token left,
        Token right
    ) => left.Equals(right);

    /// <summary>
    /// Compares two tokens by category, source text and range.
    /// </summary>
    /// <param name="left">The first token.</param>
    /// <param name="right">The second token.</param>
    /// <returns>True when token equality fails.</returns>
    public static bool operator !=(
        Token left,
        Token right
    ) => !left.Equals(right);

    /// <summary>
    /// Compares a token's text with a string using ordinal equality.
    /// </summary>
    /// <param name="left">The token whose range is compared.</param>
    /// <param name="right">The string to compare.</param>
    /// <returns>True when the complete token text matches.</returns>
    public static bool operator ==(
        Token left,
        string right
    ) => left.IsString(right);

    /// <summary>
    /// Compares a token's text with a string using ordinal equality.
    /// </summary>
    /// <param name="left">The token whose range is compared.</param>
    /// <param name="right">The string to compare.</param>
    /// <returns>True when the complete token text does not match.</returns>
    public static bool operator !=(
        Token left,
        string right
    ) => !left.IsString(right);

    /// <summary>
    /// Compares a token's range with a single UTF-16 character.
    /// </summary>
    /// <param name="left">The token whose range is compared.</param>
    /// <param name="right">The character to compare.</param>
    /// <returns>True when the range contains exactly the specified character.</returns>
    public static bool operator ==(
        Token left,
        char right
    ) => left.IsChar(right);

    /// <summary>
    /// Compares a token's range with a single UTF-16 character.
    /// </summary>
    /// <param name="left">The token whose range is compared.</param>
    /// <param name="right">The character to compare.</param>
    /// <returns>True when the range does not contain exactly the specified character.</returns>
    public static bool operator !=(
        Token left,
        char right
    ) => !left.IsChar(right);

    /// <summary>
    /// Compares the stored keyword classification, independently of the token's broad category.
    /// </summary>
    /// <param name="left">The token carrying keyword metadata.</param>
    /// <param name="right">The keyword classification to compare.</param>
    /// <returns>True when the stored keyword classifications match.</returns>
    public static bool operator ==(
        Token left,
        KeywordType right
    ) => left.keywordType == right;

    /// <summary>
    /// Compares the stored keyword classification, independently of the token's broad category.
    /// </summary>
    /// <param name="left">The token carrying keyword metadata.</param>
    /// <param name="right">The keyword classification to compare.</param>
    /// <returns>True when the stored keyword classifications differ.</returns>
    public static bool operator !=(
        Token left,
        KeywordType right
    ) => left.keywordType != right;
}
