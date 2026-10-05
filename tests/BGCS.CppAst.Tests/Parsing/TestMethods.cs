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
                    Assert.False(compilation.hasErrors);

                    Assert.Single(compilation.classes);

                    var cppClass = compilation.classes[0];
                    Assert.Equal("MyClass0", cppClass.name);

                    var methods = cppClass.functions;
                    Assert.Equal(2, methods.Count);

                    Assert.Equal("public void method0()", methods[0].ToString());
                    Assert.Equal("private static void method1()", methods[1].ToString());
                }
            );
        }
    }
}