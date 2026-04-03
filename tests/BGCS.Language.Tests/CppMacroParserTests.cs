using BGCS.Language;
using BGCS.Language.Cpp;
using BGCS.Language.Cpp.Analysers;
using Xunit;

namespace BGCS.Language.Tests;

public class CppMacroParserTests
{
    [Fact]
    public void Parse_NestedFunctionCall_ShouldMatchSyntaxTree()
    {
        const string input = "FOO(1, BAR(2,3))";
        CppMacroParser parser = new();
        var result = parser.Parse(input, "");

        Assert.NotNull(result.SyntaxTree);
        Assert.False(result.Diagnostics.HasErrors, result.Diagnostics.ToString());

        FunctionCallNode bar = new("BAR");
        bar.AddChild(new ValueNode("2", LiteralType.Number, NumberType.Int));
        bar.AddChild(new ValueNode("3", LiteralType.Number, NumberType.Int));

        FunctionCallNode foo = new("FOO");
        foo.AddChild(new ValueNode("1", LiteralType.Number, NumberType.Int));
        foo.AddChild(bar);

        ExpressionNode expr = new();
        expr.AddChild(foo);

        SyntaxTree expected = new(new RootNode(new() { expr }));

        Assert.Equal(expected.BuildDebugTree(), result.SyntaxTree!.BuildDebugTree());
    }

    [Fact]
    public void Parse_GroupingAndUnary_ShouldMatchSyntaxTree()
    {
        const string input = "((A|B) & ~(C))";
        CppMacroParser parser = new();
        var result = parser.Parse(input, "");

        Assert.NotNull(result.SyntaxTree);
        Assert.False(result.Diagnostics.HasErrors, result.Diagnostics.ToString());

        OperatorNode or = new("|");
        or.AddChild(new VariableNode("A"));
        or.AddChild(new VariableNode("B"));
        GroupNode leftGroup = new();
        leftGroup.AddChild(or);

        GroupNode cGroup = new();
        cGroup.AddChild(new VariableNode("C"));
        OperatorNode not = new("~");
        not.AddChild(cGroup);

        OperatorNode and = new("&");
        and.AddChild(leftGroup);
        and.AddChild(not);

        GroupNode outerGroup = new();
        outerGroup.AddChild(and);

        ExpressionNode expr = new();
        expr.AddChild(outerGroup);

        SyntaxTree expected = new(new RootNode(new() { expr }));

        Assert.Equal(expected.BuildDebugTree(), result.SyntaxTree!.BuildDebugTree());
    }

    [Theory]
    [InlineData("(1u<<14)")]
    [InlineData("SDL_VERSIONNUM(SDL_MAJOR_VERSION,SDL_MINOR_VERSION,SDL_PATCHLEVEL)")]
    [InlineData("((Uint16)0xFFFF)")]
    [InlineData("__cdecl")]
    public void Parse_CommonMacroExpressions_ShouldSucceed(string input)
    {
        CppMacroParser parser = new();
        var result = parser.Parse(input, "");

        Assert.NotNull(result.SyntaxTree);
        Assert.False(result.Diagnostics.HasErrors, result.Diagnostics.ToString());
    }

    [Theory]
    [InlineData("1 + ")]
    [InlineData("CALL(")]
    [InlineData(")BROKEN(")]
    public void Parse_InvalidMacroExpressions_ShouldFail(string input)
    {
        CppMacroParser parser = new();
        var result = parser.Parse(input, "");

        Assert.True(result.Diagnostics.HasErrors);
        Assert.Null(result.SyntaxTree);
    }
}
