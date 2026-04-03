using System.Linq;
using BGCS.Language;
using Xunit;

namespace BGCS.Language.Tests;

public class LexerTests
{
    private static void AssertNoErrors(LexerResult result)
    {
        Assert.False(result.Diagnostics.HasErrors, result.Diagnostics.ToString());
    }

    [Fact]
    public void Lexer_OperatorStream_ShouldMatchTokenKinds()
    {
        string input = "blah + blah;";
        Lexer lexer = new();

        var result = lexer.Tokenize(input, "");
        AssertNoErrors(result);

        var kinds = result.Tokens.Select(x => x.Type).ToArray();
        Assert.Equal(
        [
            TokenType.Identifier,
            TokenType.Operator,
            TokenType.Identifier,
            TokenType.Punctuation
        ], kinds);
    }

    [Fact]
    public void Lexer_StringLiteral_ShouldTokenize()
    {
        string input = "\"Hello World\"";
        Lexer lexer = new();

        var result = lexer.Tokenize(input, "");
        AssertNoErrors(result);

        Assert.Single(result.Tokens);
        Assert.Equal(TokenType.Literal, result.Tokens[0].Type);
        Assert.Equal("Hello World", result.Tokens[0].AsString());
    }

    [Fact]
    public void Lexer_UnclosedString_ShouldReportError()
    {
        string input = "\"string";
        Lexer lexer = new();

        var result = lexer.Tokenize(input, "");
        Assert.True(result.Diagnostics.HasErrors);
    }

    [Fact]
    public void Lexer_LineComment_ShouldProduceCommentToken()
    {
        string input = "value //comment\nnext";
        Lexer lexer = new();

        var result = lexer.Tokenize(input, "");
        AssertNoErrors(result);

        var tokens = result.Tokens;
        Assert.NotNull(tokens);
        Assert.Contains(tokens!, x => x.Type == TokenType.Comment && x.AsString() == "comment");
    }

    [Fact]
    public void Lexer_BlockComment_ShouldProduceCommentToken()
    {
        string input = "a /* block */ b";
        Lexer lexer = new();

        var result = lexer.Tokenize(input, "");
        AssertNoErrors(result);

        var tokens = result.Tokens;
        Assert.NotNull(tokens);
        Assert.Contains(tokens!, x => x.Type == TokenType.Comment && x.AsString() == " block ");
    }

    [Fact]
    public void Lexer_CharLiteral_ShouldProduceCharLiteralToken()
    {
        string input = "'x'";
        Lexer lexer = new();

        var result = lexer.Tokenize(input, "");
        AssertNoErrors(result);

        Assert.Single(result.Tokens!);
        Assert.Equal(TokenType.Literal, result.Tokens![0].Type);
        Assert.Equal(LiteralType.Char, result.Tokens[0].LiteralType);
        Assert.Equal("x", result.Tokens[0].AsString());
    }

    [Fact]
    public void Lexer_KeywordsAndNumbers_ShouldClassifyTokenTypes()
    {
        string input = "public class A { return 42; }";
        Lexer lexer = new();

        var result = lexer.Tokenize(input, "");
        AssertNoErrors(result);

        var tokens = result.Tokens!;
        Assert.Contains(tokens, x => x.Type == TokenType.Keyword && x.KeywordType == KeywordType.Public);
        Assert.Contains(tokens, x => x.Type == TokenType.Keyword && x.KeywordType == KeywordType.Class);
        Assert.Contains(tokens, x => x.Type == TokenType.Keyword && x.KeywordType == KeywordType.Return);
        Assert.Contains(tokens, x => x.Type == TokenType.Literal && x.LiteralType == LiteralType.Number && x.AsString() == "42");
    }

    [Fact]
    public void Lexer_MethodSignatureWithSingleLetterIdentifier_ShouldKeepIdentifier()
    {
        string input = "void M(int x) { }";
        Lexer lexer = new();

        var result = lexer.Tokenize(input, "");
        AssertNoErrors(result);

        var tokens = result.Tokens!;
        Assert.Contains(tokens, x => x.Type == TokenType.Keyword && x.KeywordType == KeywordType.Void);
        Assert.Contains(tokens, x => x.Type == TokenType.Identifier && x.AsString() == "M");
    }

    [Fact]
    public void Lexer_UnclosedBlockComment_ShouldReportError()
    {
        string input = "/* unclosed";
        Lexer lexer = new();

        var result = lexer.Tokenize(input, "");
        Assert.True(result.Diagnostics.HasErrors);
        Assert.Null(result.Tokens);
    }
}
