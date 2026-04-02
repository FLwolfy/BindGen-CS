using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Attributes;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Expressions;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Model.Metadata;
using BGCS.CppAst.Model.Templates;
using BGCS.CppAst.Model.Types;
using BGCS.CppAst.Parsing;
using BGCS.CppAst.Extensions;
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
                    Assert.False(compilation.HasErrors);

                    Assert.Equal(6, compilation.Macros.Count);

                    {
                        var macro = compilation.Macros[0];
                        Assert.Equal("MACRO0", macro.Name);
                        Assert.Equal("", macro.Value);
                        Assert.Equal(0, macro.Tokens.Count);
                        Assert.Null(macro.Parameters);
                    }

                    {
                        var macro = compilation.Macros[1];
                        Assert.Equal("MACRO1", macro.Name);
                        Assert.Equal("1", macro.Value);
                        Assert.Equal(1, macro.Tokens.Count);
                        Assert.Equal("1", macro.Tokens[0].Text);
                        Assert.Equal(CppTokenKind.Literal, macro.Tokens[0].Kind);
                        Assert.Null(macro.Parameters);
                    }

                    {
                        var macro = compilation.Macros[2];
                        Assert.Equal("MACRO2", macro.Name);
                        Assert.Equal("", macro.Value);
                        Assert.NotNull(macro.Parameters);
                        Assert.Equal(1, macro.Parameters.Count);
                        Assert.Equal("x", macro.Parameters[0]);
                    }

                    {
                        var macro = compilation.Macros[3];
                        Assert.Equal("MACRO3", macro.Name);
                        Assert.Equal("x+1", macro.Value);
                        Assert.NotNull(macro.Parameters);
                        Assert.Equal(1, macro.Parameters.Count);
                        Assert.Equal("x", macro.Parameters[0]);

                        Assert.Equal(3, macro.Tokens.Count);
                        Assert.Equal("x", macro.Tokens[0].Text);
                        Assert.Equal("+", macro.Tokens[1].Text);
                        Assert.Equal("1", macro.Tokens[2].Text);
                        Assert.Equal(CppTokenKind.Identifier, macro.Tokens[0].Kind);
                        Assert.Equal(CppTokenKind.Punctuation, macro.Tokens[1].Kind);
                        Assert.Equal(CppTokenKind.Literal, macro.Tokens[2].Kind);
                    }

                    {
                        var macro = compilation.Macros[4];
                        Assert.Equal("MACRO4", macro.Name);
                        Assert.Equal("(x)", macro.Value);
                        Assert.Null(macro.Parameters);

                        Assert.Equal(3, macro.Tokens.Count);
                        Assert.Equal("(", macro.Tokens[0].Text);
                        Assert.Equal("x", macro.Tokens[1].Text);
                        Assert.Equal(")", macro.Tokens[2].Text);
                        Assert.Equal(CppTokenKind.Punctuation, macro.Tokens[0].Kind);
                        Assert.Equal(CppTokenKind.Identifier, macro.Tokens[1].Kind);
                        Assert.Equal(CppTokenKind.Punctuation, macro.Tokens[2].Kind);
                    }

                    {
                        var macro = compilation.Macros[5];
                        Assert.Equal("MACRO5", macro.Name);
                        Assert.Equal("1", macro.Value);
                        Assert.Null(macro.Parameters);

                        Assert.Equal(2, macro.Tokens.Count);
                        Assert.Equal("1", macro.Tokens[0].Text);
                        Assert.Equal("/* with a comment */", macro.Tokens[1].Text);
                        Assert.Equal(CppTokenKind.Literal, macro.Tokens[0].Kind);
                        Assert.Equal(CppTokenKind.Comment, macro.Tokens[1].Kind);
                    }
                }
                , new CppParserOptions().EnableMacros()
            );
        }
    }
}