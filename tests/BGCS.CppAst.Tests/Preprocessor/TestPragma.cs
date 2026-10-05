using System;
using Xunit;
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
                    Assert.False(compilation.hasErrors);
                    foreach (var message in compilation.diagnostics.messages)
                    {
                        Console.WriteLine(message);
                    }
                    Assert.Single(compilation.classes);
                }
            );
        }
    }
}
