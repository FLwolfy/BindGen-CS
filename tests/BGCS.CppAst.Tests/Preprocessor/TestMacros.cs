using BGCS.CppAst.Model.Expressions;
using BGCS.CppAst.Parsing;
using Xunit;
namespace BGCS.CppAst.Tests
{
    public class TestMacros : InlineTestBase
    {
        [Fact]
        public void TestSimple()
        {
            ParseAssert(@"
#define MACRO0
#define MACRO1 1
#define MACRO2(x)
#define MACRO3(x) x + 1
#define MACRO4 (x)
#define MACRO5 1 /* with a comment */
",
                compilation =>
                {
                    Assert.False(compilation.hasErrors);

                    Assert.Equal(6, compilation.macros.Count);

                    {
                        var macro = compilation.macros[0];
                        Assert.Equal("MACRO0", macro.name);
                        Assert.Equal("", macro.value);
                        Assert.Empty(macro.tokens);
                        Assert.Null(macro.parameters);
                    }

                    {
                        var macro = compilation.macros[1];
                        Assert.Equal("MACRO1", macro.name);
                        Assert.Equal("1", macro.value);
                        Assert.Single(macro.tokens);
                        Assert.Equal("1", macro.tokens[0].text);
                        Assert.Equal(CppTokenKind.Literal, macro.tokens[0].kind);
                        Assert.Null(macro.parameters);
                    }

                    {
                        var macro = compilation.macros[2];
                        Assert.Equal("MACRO2", macro.name);
                        Assert.Equal("", macro.value);
                        Assert.NotNull(macro.parameters);
                        Assert.Single(macro.parameters);
                        Assert.Equal("x", macro.parameters[0]);
                    }

                    {
                        var macro = compilation.macros[3];
                        Assert.Equal("MACRO3", macro.name);
                        Assert.Equal("x+1", macro.value);
                        Assert.NotNull(macro.parameters);
                        Assert.Single(macro.parameters);
                        Assert.Equal("x", macro.parameters[0]);

                        Assert.Equal(3, macro.tokens.Count);
                        Assert.Equal("x", macro.tokens[0].text);
                        Assert.Equal("+", macro.tokens[1].text);
                        Assert.Equal("1", macro.tokens[2].text);
                        Assert.Equal(CppTokenKind.Identifier, macro.tokens[0].kind);
                        Assert.Equal(CppTokenKind.Punctuation, macro.tokens[1].kind);
                        Assert.Equal(CppTokenKind.Literal, macro.tokens[2].kind);
                    }

                    {
                        var macro = compilation.macros[4];
                        Assert.Equal("MACRO4", macro.name);
                        Assert.Equal("(x)", macro.value);
                        Assert.Null(macro.parameters);

                        Assert.Equal(3, macro.tokens.Count);
                        Assert.Equal("(", macro.tokens[0].text);
                        Assert.Equal("x", macro.tokens[1].text);
                        Assert.Equal(")", macro.tokens[2].text);
                        Assert.Equal(CppTokenKind.Punctuation, macro.tokens[0].kind);
                        Assert.Equal(CppTokenKind.Identifier, macro.tokens[1].kind);
                        Assert.Equal(CppTokenKind.Punctuation, macro.tokens[2].kind);
                    }

                    {
                        var macro = compilation.macros[5];
                        Assert.Equal("MACRO5", macro.name);
                        Assert.Equal("1", macro.value);
                        Assert.Null(macro.parameters);

                        Assert.Equal(2, macro.tokens.Count);
                        Assert.Equal("1", macro.tokens[0].text);
                        Assert.Equal("/* with a comment */", macro.tokens[1].text);
                        Assert.Equal(CppTokenKind.Literal, macro.tokens[0].kind);
                        Assert.Equal(CppTokenKind.Comment, macro.tokens[1].kind);
                    }
                }
                , new CppParserOptions().EnableMacros()
            );
        }
    }
}
