using System;
using BGCS.Language.Lexing;
using Xunit;

namespace BGCS.Language.Tests;

public sealed class TokenContractTests
{
    [Fact]
    public void EmptyToken_CanBeFormattedAndComparedWithoutIndexingAnEmptyRange()
    {
        var token = new Token(TokenType.Identifier, 0, 0, string.Empty);

        Assert.Equal("Identifier \t ", token.ToString());
        Assert.True(token.IsString(string.Empty));
        Assert.False(token.IsChar('a'));
        Assert.Throws<ArgumentNullException>(() => token.IsString(null!));
    }

    [Fact]
    public void EqualityAndTextComparison_HaveDistinctDocumentedContracts()
    {
        var identifier = new Token(TokenType.Identifier, 1, 3, " abc ");
        var keyword = new Token(TokenType.Keyword, 1, 3, " abc ");

        Assert.True(identifier == "abc");
        Assert.True(keyword == "abc");
        Assert.True(identifier != keyword);
        Assert.Equal("abc", identifier.AsString());
    }
}
