using System.IO;
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
// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace BGCS.CppAst.Tests
{
    public class TestComments : InlineTestBase
    {
        [Fact]
        public void TestSimple()
        {
            ParseAssert(@"
// This is a header of the file

// This is a comment of f0
int f0;

// This is a comment of function0
void function0();

// This is a comment of MyStruct0
struct MyStruct0
{
};

// This is a comment of Enum0
enum Enum0
{
    // This is a comment of Enum0_item0
    Enum0_item0,
    // This is a comment of Enum0_item1
    Enum0_item1,
};
",
                compilation =>
                {
                    Assert.False(compilation.HasErrors);

                    var cppElements = compilation.Children.ToList();
                    Assert.Equal(4, cppElements.Count);

                    var results = cppElements.Select(x => (x.Comment.ToString(), x.GetType())).ToList();

                    Assert.Equal(1, compilation.Enums.Count);

                    results.AddRange(compilation.Enums[0].Children.Select(x => (x.Comment.ToString(), x.GetType())));

                    var expectedResults = new List<(string, Type)>()
                    {
                        ("This is a comment of Enum0", typeof(CppEnum)),
                        ("This is a comment of MyStruct0", typeof(CppClass)),
                        ("This is a comment of f0", typeof(CppField)),
                        ("This is a comment of function0", typeof(CppFunction)),
                        ("This is a comment of Enum0_item0", typeof(CppEnumItem)),
                        ("This is a comment of Enum0_item1", typeof(CppEnumItem)),
                    };

                    Assert.Equal(expectedResults, results);
                }
            );
        }

        [Fact]
        public void TestComplex()
        {
            ParseAssert(@"
/// This is a comment of function1.
///
/// With more `details` in the <b>comment</b>.
/// And another line with \a x and \a y in italics
///
/// @see function1
///
/// @param a this is a parameter comment
/// @param b this is b parameter comment
/// @return an integer value
///
/// \code{.cpp}
/// this is a comment
/// \endcode
///
/// \exception FileNotFoundException if file does not exist.
int function1(int a, int b);

", compilation =>
            {
                Assert.False(compilation.HasErrors);

                var expectedText = @"This is a comment of function1.
With more `details` in the <b>comment</b>.
And another line with @a x and @a y in italics

@see function1

@param a this is a parameter comment
@param b this is b parameter comment
@return an integer value

@code {.cpp}
 this is a comment
@endcode

@exception FileNotFoundException if file does not exist.";

                Assert.Equal(1, compilation.Functions.Count);
                var resultText = compilation.Functions[0].Comment?.ToString();

                expectedText = expectedText.Replace("\r\n", "\n");
                resultText = resultText?.Replace("\r\n", "\n");
                Assert.Equal(expectedText, resultText);
            });
        }

        [Fact]
        public void TestParen()
        {
            ParseAssert(@"
// [infinite loop)
int function1(int a, int b);
", compilation =>
            {
                Assert.False(compilation.HasErrors);

                var expectedText = @"[infinite loop)";

                Assert.Equal(1, compilation.Functions.Count);
                var resultText = compilation.Functions[0].Comment?.ToString();

                expectedText = expectedText.Replace("\r\n", "\n");
                resultText = resultText?.Replace("\r\n", "\n");
                Assert.Equal(expectedText, resultText);
            },
            new CppParserOptions() { ParseTokenAttributes = true });
        }
    }
}