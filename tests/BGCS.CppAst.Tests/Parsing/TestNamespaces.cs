using System.Collections.Generic;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Interfaces;
using Xunit;
// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace BGCS.CppAst.Tests
{
    public class TestNamespaces : InlineTestBase
    {
        [Fact]
        public void TestSimple()
        {
            ParseAssert(@"
namespace A
{
    namespace B {
        int b;
    }
};

namespace A
{
    int a;
};

namespace A::B::C
{
    int c;
};
",
                compilation =>
                {
                    Assert.False(compilation.hasErrors);

                    var namespaces = new List<string>() { "A", "B", "C" };

                    ICppGlobalDeclarationContainer container = compilation;

                    foreach (var nsName in namespaces)
                    {
                        Assert.Single(container.namespaces);
                        var ns = container.namespaces[0];
                        Assert.Equal(nsName, ns.name);
                        Assert.Single(ns.fields);
                        Assert.Equal(nsName.ToLowerInvariant(), ns.fields[0].name);

                        // Continue on the sub-namespaces
                        container = ns;
                    }
                }
            );
        }

        [Fact]
        public void TestNamespacedTypedef()
        {
            ParseAssert(@"
namespace A
{
    typedef int (*a)(int b);
}
A::a c;
",
                compilation =>
                {
                    Assert.False(compilation.hasErrors);

                    Assert.Single(compilation.namespaces);
                    ICppGlobalDeclarationContainer container = compilation.namespaces[0];
                    Assert.Single(container.typedefs);
                    Assert.Single(compilation.fields);

                    CppTypedef typedef = container.typedefs[0];
                    CppField field = compilation.fields[0];

                    Assert.Equal(typedef, field.type);
                }
            );
        }



        [Fact]
        public void TestNamespaceFindByFullName()
        {
            var text = @"
namespace A
{
// Test using Template
template <typename T>
struct MyStruct;

using MyStructInt = MyStruct<int>;
}

";

            ParseAssert(text,
                compilation =>
                {
                    Assert.False(compilation.hasErrors);

                    Assert.Single(compilation.namespaces);

                    var cppStruct = Assert.IsType<CppClass>(compilation.FindByFullName<CppClass>("A::MyStruct"));
                    Assert.Equal(compilation.namespaces[0].classes[0], cppStruct);
                }
            );
        }

        [Fact]
        public void TestInlineNamespace()
        {
            var text = @"
namespace A
{

inline namespace __1
{
    // Test using Template
    template <typename T>
    struct MyStruct;

    using MyStructInt = MyStruct<int>;
}

}

";

            ParseAssert(text,
                compilation =>
                {
                    Assert.False(compilation.hasErrors);

                    Assert.Single(compilation.namespaces);

                    var inlineNs = compilation.namespaces[0].namespaces[0];
                    Assert.Equal("__1", inlineNs.name);
                    Assert.True(inlineNs.isInlineNamespace);

                    var cppStruct = Assert.IsType<CppClass>(compilation.FindByFullName<CppClass>("A::MyStruct"));
                    Assert.Equal(inlineNs.classes[0], cppStruct);
                    Assert.Equal("A::MyStruct<T>", cppStruct.fullName);

                    var cppTypedef = Assert.IsType<CppTypedef>(compilation.FindByFullName<CppTypedef>("A::MyStructInt"));
                    var cppStructInt = Assert.IsType<CppClass>(cppTypedef.elementType);
                    //So now we can use this full name in exporter convenience.
                    Assert.Equal("A::MyStruct<int>", cppStructInt.fullName);
                }
            );
        }
    }
}
