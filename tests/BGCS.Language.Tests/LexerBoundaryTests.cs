using System.Linq;
using BGCS.Core.Text;
using BGCS.Language.Lexing;
using Xunit;

namespace BGCS.Language.Tests;

public sealed class LexerBoundaryTests
{
    [Theory]
    [InlineData("value//comment\nnext", "value|comment|next")]
    [InlineData("value/*comment*/next", "value|comment|next")]
    [InlineData("value\"text\"next", "value|text|next")]
    [InlineData("value'x'next", "value|x|next")]
    [InlineData("\"\"/**/", "|")]
    [InlineData("value-flag", "value|-|flag")]
    [InlineData("1E-2+3", "1E-2|+|3")]
    public void AdjacentTokensRetainEverySourceRange(
        string source,
        string expected
    ) {
        LexerResult result = new Lexer().Tokenize(source, "fixture");
        Assert.False(result.diagnostics.hasErrors, result.diagnostics.ToString());
        Assert.Equal(expected, string.Join("|", result.tokens!.Select(token => token.AsString())));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void PositionsIdentifyRangeStartsAcrossLineEndings(string lineEnding)
    {
        string source = "first/*line" + lineEnding + "comment*/ second" + lineEnding + "  third";
        LexerResult result = new Lexer().Tokenize(source, "fixture");
        Assert.False(result.diagnostics.hasErrors);
        Assert.Equal(new[] { "first", "line" + lineEnding + "comment", "second", "third" }, result.tokens!.Select(token => token.AsString()));
        Token first = result.tokens![0];
        Assert.Equal(0, first.location.column);
        Token comment = result.tokens[1];
        Assert.Equal(7, comment.location.column);
        Assert.Equal(0, comment.location.line);
        Token second = result.tokens[2];
        Assert.Equal(1, second.location.line);
        Assert.Equal(10, second.location.column);
        Token third = result.tokens[3];
        Assert.Equal(2, third.location.line);
        Assert.Equal(2, third.location.column);
        Assert.All(result.tokens, token => Assert.Equal(token.start, token.location.offset));
    }

    [Fact]
    public void EscapedBackslashDoesNotHideTheClosingQuote()
    {
        LexerResult result = new Lexer().Tokenize("\"path\\\\\" next", "fixture");
        Assert.False(result.diagnostics.hasErrors);
        Assert.Equal(new[] { "path\\\\", "next" }, result.tokens!.Select(token => token.AsString()));
    }

    [Theory]
    [InlineData("0b101", NumberType.Int)]
    [InlineData("0xFFUL", NumberType.ULong)]
    [InlineData("1e2", NumberType.Double)]
    [InlineData("1E-2f", NumberType.Float)]
    [InlineData(".5", NumberType.Double)]
    [InlineData("12LU", NumberType.ULong)]
    public void CompleteNumericSyntaxDeterminesTheCategory(
        string spelling,
        NumberType expected
    ) {
        Assert.True(Lexer.IsNumber(spelling, out NumberType actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("0b102")]
    [InlineData("1..2")]
    [InlineData("1e-")]
    [InlineData("0x")]
    [InlineData("+-1")]
    [InlineData("1.0UL")]
    [InlineData("١")]
    public void MalformedNumericSyntaxHasNoCategory(string spelling)
    {
        Assert.False(Lexer.IsNumber(spelling, out NumberType actual));
        Assert.Equal(NumberType.None, actual);
    }

    [Theory]
    [InlineData("1.5", BGCS.Language.Lexing.NumberParseOptions.AllowExponent)]
    [InlineData("1e-2", BGCS.Language.Lexing.NumberParseOptions.AllowDecimal)]
    [InlineData("1U", BGCS.Language.Lexing.NumberParseOptions.None)]
    [InlineData("0xFF", BGCS.Language.Lexing.NumberParseOptions.None)]
    public void DisabledNumericSyntaxIsRejected(
        string spelling,
        BGCS.Language.Lexing.NumberParseOptions options
    ) => Assert.False(Lexer.IsNumber(spelling, out _, options));
}
