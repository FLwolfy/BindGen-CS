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
// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace BGCS.CppAst.Tests
{
    public class TestMethods : InlineTestBase
    {
        [Fact]
        public void TestSimple()
        {
            ParseAssert(@"
class MyClass0
{
    public:
    void method0();

    private:
    static void method1();
};
",
                compilation =>
                {
                    Assert.False(compilation.HasErrors);

                    Assert.Equal(1, compilation.Classes.Count);

                    var cppClass = compilation.Classes[0];
                    Assert.Equal("MyClass0", cppClass.Name);

                    var methods = cppClass.Functions;
                    Assert.Equal(2, methods.Count);

                    Assert.Equal("public void method0()", methods[0].ToString());
                    Assert.Equal("private static void method1()", methods[1].ToString());
                }
            );
        }
    }
}