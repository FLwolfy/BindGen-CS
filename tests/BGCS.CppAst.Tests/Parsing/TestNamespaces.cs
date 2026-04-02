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

using System.Collections.Generic;

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
                    Assert.False(compilation.HasErrors);

                    var namespaces = new List<string>() { "A", "B", "C" };

                    ICppGlobalDeclarationContainer container = compilation;

                    foreach (var nsName in namespaces)
                    {
                        Assert.Equal(1, container.Namespaces.Count);
                        var ns = container.Namespaces[0];
                        Assert.Equal(nsName, ns.Name);
                        Assert.Equal(1, ns.Fields.Count);
                        Assert.Equal(nsName.ToLowerInvariant(), ns.Fields[0].Name);

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
                    Assert.False(compilation.HasErrors);

                    Assert.Equal(1, compilation.Namespaces.Count);
                    ICppGlobalDeclarationContainer container = compilation.Namespaces[0];
                    Assert.Equal(1, container.Typedefs.Count);
                    Assert.Equal(1, compilation.Fields.Count);

                    CppTypedef typedef = container.Typedefs[0];
                    CppField field = compilation.Fields[0];

                    Assert.Equal(typedef, field.Type);
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
                    Assert.False(compilation.HasErrors);

                    Assert.Equal(1, compilation.Namespaces.Count);

                    var cppStruct = compilation.FindByFullName<CppClass>("A::MyStruct");
                    Assert.Equal(compilation.Namespaces[0].Classes[0], cppStruct);
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
                    Assert.False(compilation.HasErrors);

                    Assert.Equal(1, compilation.Namespaces.Count);

                    var inlineNs = compilation.Namespaces[0].Namespaces[0];
                    Assert.Equal(inlineNs.Name, "__1");
                    Assert.Equal(true, inlineNs.IsInlineNamespace);

                    var cppStruct = compilation.FindByFullName<CppClass>("A::MyStruct");
                    Assert.Equal(inlineNs.Classes[0], cppStruct);
                    Assert.Equal(cppStruct.FullName, "A::MyStruct<T>");

                    var cppTypedef = compilation.FindByFullName<CppTypedef>("A::MyStructInt");
                    var cppStructInt = cppTypedef.ElementType as CppClass;
                    //So now we can use this full name in exporter convenience.
                    Assert.Equal(cppStructInt.FullName, "A::MyStruct<int>");
                }
            );
        }
    }
}