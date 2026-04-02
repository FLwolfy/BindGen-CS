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
using System;

namespace BGCS.CppAst.Tests
{
    public class TestPragma : InlineTestBase
    {
        [Fact]
        public void TestPragmaOnce()
        {
            ParseAssert(@"
#include ""test_pragma_root.h""
",
                compilation =>
                {
                    Assert.False(compilation.HasErrors);
                    foreach (var message in compilation.Diagnostics.Messages)
                    {
                        Console.WriteLine(message);
                    }
                    Assert.Equal(1, compilation.Classes.Count);
                }
            );
        }
    }
}