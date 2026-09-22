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
                    Assert.False(compilation.HasErrors);

                    //annotate attribute support on global function
                    var cppFunc = compilation.Functions[0];
                    Assert.Single(cppFunc.Attributes);
                    Assert.Equal(AttributeKind.AnnotateAttribute, cppFunc.Attributes[0].Kind);
                    Assert.Equal("script, is_browsable=true, desc=\"a function\"", cppFunc.Attributes[0].Arguments);

                    //annotate attribute support on enum
                    var cppEnum = compilation.Enums[0];
                    Assert.Single(cppEnum.Attributes);
                    Assert.Equal(AttributeKind.AnnotateAttribute, cppEnum.Attributes[0].Kind);
                    Assert.Equal("script, is_browsable=true, desc=\"a enum\"", cppEnum.Attributes[0].Arguments);

                    //annotate attribute support on class
                    var cppClass = compilation.Classes[0];
                    Assert.Single(cppClass.Attributes);
                    Assert.Equal(AttributeKind.AnnotateAttribute, cppClass.Attributes[0].Kind);
                    Assert.Equal("script, is_browsable=true, desc=\"a class\"", cppClass.Attributes[0].Arguments);

                    Assert.Single(cppClass.Functions);
                    var memFunc = cppClass.Functions[0];
                    Assert.Single(memFunc.Attributes);
                    Assert.Equal("desc=\"a member function\"", memFunc.Attributes[0].Arguments);


                    Assert.Single(cppClass.Fields);
                    var memField = cppClass.Fields[0];
                    Assert.Single(memField.Attributes);
                    Assert.Equal("desc=\"a member field\"", memField.Attributes[0].Arguments);
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
                    Assert.False(compilation.HasErrors);

                    //annotate attribute support on namespace
                    var ns = compilation.Namespaces[0];
                    Assert.Single(ns.Attributes);
                    Assert.Equal(AttributeKind.AnnotateAttribute, ns.Attributes[0].Kind);
                    Assert.Equal("script, is_browsable=true, desc=\"a namespace test\"", ns.Attributes[0].Arguments);

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
                    Assert.False(compilation.HasErrors);

                    //annotate attribute support on namespace
                    var func = compilation.Functions[0];
                    Assert.Single(func.Attributes);
                    Assert.Equal(AttributeKind.AnnotateAttribute, func.Attributes[0].Kind);
                    Assert.Equal("id=12345, desc=\"a function with macro\"", func.Attributes[0].Arguments);

                }
            );
        }
    }
}
