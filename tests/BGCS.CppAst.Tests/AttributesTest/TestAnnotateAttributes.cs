using BGCS.CppAst.Model.Attributes;
using Xunit;
// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace BGCS.CppAst.Tests
{
    public class TestAnnotateAttributes : InlineTestBase
    {
        [Fact]
        public void TestAnnotateAttribute()
        {
            var text = @"

#if !defined(__cppast) 
#define __cppast(...)
#endif

__cppast(script, is_browsable=true, desc=""a function"")
void TestFunc()
{
}

enum class __cppast(script, is_browsable=true, desc=""a enum"") TestEnum
{
};

class __cppast(script, is_browsable=true, desc=""a class"") TestClass
{
  public:
    __cppast(desc=""a member function"")
    void TestMemberFunc();

    __cppast(desc=""a member field"")
    int X;
};
";

            ParseAssert(text,
                compilation =>
                {
                    Assert.False(compilation.hasErrors);

                    //annotate attribute support on global function
                    var cppFunc = compilation.functions[0];
                    Assert.Single(cppFunc.attributes);
                    Assert.Equal(AttributeKind.AnnotateAttribute, cppFunc.attributes[0].kind);
                    Assert.Equal("script, is_browsable=true, desc=\"a function\"", cppFunc.attributes[0].arguments);

                    //annotate attribute support on enum
                    var cppEnum = compilation.enums[0];
                    Assert.Single(cppEnum.attributes);
                    Assert.Equal(AttributeKind.AnnotateAttribute, cppEnum.attributes[0].kind);
                    Assert.Equal("script, is_browsable=true, desc=\"a enum\"", cppEnum.attributes[0].arguments);

                    //annotate attribute support on class
                    var cppClass = compilation.classes[0];
                    Assert.Single(cppClass.attributes);
                    Assert.Equal(AttributeKind.AnnotateAttribute, cppClass.attributes[0].kind);
                    Assert.Equal("script, is_browsable=true, desc=\"a class\"", cppClass.attributes[0].arguments);

                    Assert.Single(cppClass.functions);
                    var memFunc = cppClass.functions[0];
                    Assert.Single(memFunc.attributes);
                    Assert.Equal("desc=\"a member function\"", memFunc.attributes[0].arguments);


                    Assert.Single(cppClass.fields);
                    var memField = cppClass.fields[0];
                    Assert.Single(memField.attributes);
                    Assert.Equal("desc=\"a member field\"", memField.attributes[0].arguments);
                }
            );
        }


        [Fact]
        public void TestAnnotateAttributeInNamespace()
        {
            var text = @"

#if !defined(__cppast)
#define __cppast(...)
#endif

namespace __cppast(script, is_browsable=true, desc=""a namespace test"") TestNs{

}

";

            ParseAssert(text,
                compilation =>
                {
                    Assert.False(compilation.hasErrors);

                    //annotate attribute support on namespace
                    var ns = compilation.namespaces[0];
                    Assert.Single(ns.attributes);
                    Assert.Equal(AttributeKind.AnnotateAttribute, ns.attributes[0].kind);
                    Assert.Equal("script, is_browsable=true, desc=\"a namespace test\"", ns.attributes[0].arguments);

                }
            );
        }

        [Fact]
        public void TestAnnotateAttributeWithMacro()
        {
            var text = @"

#if !defined(__cppast)
#define __cppast(...)
#endif

#define UUID() 12345

__cppast(id=UUID(), desc=""a function with macro"")
void TestFunc()
{
}

";

            ParseAssert(text,
                compilation =>
                {
                    Assert.False(compilation.hasErrors);

                    //annotate attribute support on namespace
                    var func = compilation.functions[0];
                    Assert.Single(func.attributes);
                    Assert.Equal(AttributeKind.AnnotateAttribute, func.attributes[0].kind);
                    Assert.Equal("id=12345, desc=\"a function with macro\"", func.attributes[0].arguments);

                }
            );
        }
    }
}
