using System.Collections.Generic;
using System.Linq;
using BGCS.Language.Lexing;
using Xunit;

namespace BGCS.Language.Tests;

public class LexerTests
{
    private static void AssertNoErrors(LexerResult result)
    {
        Assert.False(result.diagnostics.hasErrors, result.diagnostics.ToString());
    }

    [Fact]
    public void Lexer_OperatorStream_ShouldMatchTokenKinds()
    {
        string input = "blah + blah;";
        Lexer lexer = new();

        var result = lexer.Tokenize(input, "");
        AssertNoErrors(result);

        var tokens = Assert.IsType<List<Token>>(result.tokens);
        var kinds = tokens.Select(x => x.type).ToArray();
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

        var tokens = Assert.IsType<List<Token>>(result.tokens);
        Assert.Single(tokens);
        Assert.Equal(TokenType.Literal, tokens[0].type);
        Assert.Equal("Hello World", tokens[0].AsString());
    }

    [Fact]
    public void Lexer_UnclosedString_ShouldReportError()
    {
        string input = "\"string";
        Lexer lexer = new();

        var result = lexer.Tokenize(input, "");
        Assert.True(result.diagnostics.hasErrors);
    }

    [Fact]
    public void Lexer_LineComment_ShouldProduceCommentToken()
    {
        string input = "value //comment\nnext";
        Lexer lexer = new();

        var result = lexer.Tokenize(input, "");
        AssertNoErrors(result);

        var tokens = result.tokens;
        Assert.NotNull(tokens);
        Assert.Contains(tokens!, x => x.type == TokenType.Comment && x.AsString() == "comment");
    }

    [Fact]
    public void Lexer_BlockComment_ShouldProduceCommentToken()
    {
        string input = "a /* block */ b";
        Lexer lexer = new();

        var result = lexer.Tokenize(input, "");
        AssertNoErrors(result);

        var tokens = result.tokens;
        Assert.NotNull(tokens);
        Assert.Contains(tokens!, x => x.type == TokenType.Comment && x.AsString() == " block ");
    }

    [Fact]
    public void Lexer_CharLiteral_ShouldProduceCharLiteralToken()
    {
        string input = "'x'";
        Lexer lexer = new();

        var result = lexer.Tokenize(input, "");
        AssertNoErrors(result);

        Assert.Single(result.tokens!);
        Assert.Equal(TokenType.Literal, result.tokens![0].type);
        Assert.Equal(LiteralType.Char, result.tokens[0].literalType);
        Assert.Equal("x", result.tokens[0].AsString());
    }

    [Fact]
    public void Lexer_KeywordsAndNumbers_ShouldClassifyTokenTypes()
    {
        string input = "public class A { return 42; }";
        Lexer lexer = new();

        var result = lexer.Tokenize(input, "");
        AssertNoErrors(result);

        var tokens = result.tokens!;
        Assert.Contains(tokens, x => x.type == TokenType.Keyword && x.keywordType == KeywordType.Public);
        Assert.Contains(tokens, x => x.type == TokenType.Keyword && x.keywordType == KeywordType.Class);
        Assert.Contains(tokens, x => x.type == TokenType.Keyword && x.keywordType == KeywordType.Return);
        Assert.Contains(tokens, x => x.type == TokenType.Literal && x.literalType == LiteralType.Number && x.AsString() == "42");
    }

    [Fact]
    public void Lexer_MethodSignatureWithSingleLetterIdentifier_ShouldKeepIdentifier()
    {
        string input = "void M(int x) { }";
        Lexer lexer = new();

        var result = lexer.Tokenize(input, "");
        AssertNoErrors(result);

        var tokens = result.tokens!;
        Assert.Contains(tokens, x => x.type == TokenType.Keyword && x.keywordType == KeywordType.Void);
        Assert.Contains(tokens, x => x.type == TokenType.Identifier && x.AsString() == "M");
    }

    [Fact]
    public void Lexer_UnclosedBlockComment_ShouldReportError()
    {
        string input = "/* unclosed";
        Lexer lexer = new();

        var result = lexer.Tokenize(input, "");
        Assert.True(result.diagnostics.hasErrors);
        Assert.Null(result.tokens);
    }
}
