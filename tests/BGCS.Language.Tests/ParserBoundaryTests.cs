using System.Collections.Generic;
using System.Linq;
using BGCS.Language.Diagnostics;
using BGCS.Language.Lexing;
using BGCS.Language.Parsing;
using BGCS.Language.Syntax;
using Xunit;

namespace BGCS.Language.Tests;

public sealed class ParserBoundaryTests
{
    [Theory]
    [InlineData("x", "x")]
    [InlineData("longName", "longName")]
    [InlineData("value next", "next")]
    [InlineData("//end", "end")]
    public void TokenizationKeepsTheCompleteFinalToken(string source, string expected)
    {
        LexerResult result = new Lexer().Tokenize(source, "final.cs");
        Assert.False(result.diagnostics.hasErrors);
        Assert.Equal(expected, result.tokens!.Last().AsString());
    }

    [Fact]
    public void ScopedDispatchRejectsAnAnalyzerThatDoesNotAdvance()
    {
        List<Token> tokens = new Lexer().Tokenize("{ value }", "scope.cs").tokens!;
        var diagnostics = new DiagnosticBag();
        var context = new ParserContext(new RootNode(), new ParserOptions(), [new StalledAnalyzer()], tokens, diagnostics);

        Assert.Equal(AnalyserResult.Error, context.AnalyseScoped(new RootNode()));
        Assert.True(diagnostics.hasErrors);
        Assert.Contains(diagnostics.messages, message => message.text.Contains("forward progress"));
    }

    [Fact]
    public void FileScopedAnalysisRestoresTheEnclosingScopeAndSeekingCannotWrap()
    {
        var diagnostics = new DiagnosticBag();
        var root = new RootNode();
        List<Token> tokens = new Lexer().Tokenize("value", "file.cs").tokens!;
        var context = new ParserContext(root, new ParserOptions(), [new AdvancingAnalyzer()], tokens, diagnostics);

        Assert.Equal(AnalyserResult.Success, context.AnalyseFileScoped(new RootNode()));
        Assert.Equal(0, context.scopeDepth);
        Assert.Same(root, context.current);
        context.MoveTo(int.MinValue);
        Assert.False(context.SeekInBounds(int.MinValue));
        Assert.False(context.TryMoveNext(out _));
    }

    private sealed class StalledAnalyzer : ISyntaxAnalyzer
    {
        public AnalyserResult Analyze(ParserContext context) => AnalyserResult.Success;
    }

    private sealed class AdvancingAnalyzer : ISyntaxAnalyzer
    {
        public AnalyserResult Analyze(ParserContext context)
        {
            context.MoveNext();
            return AnalyserResult.Success;
        }
    }
}
