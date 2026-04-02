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

    public class TestMetaAttribute : InlineTestBase
    {
        [Fact]
        public void TestNamespaceMetaAttribute()
        {
            ParseAssert(@"

#if !defined(__cppast)
#define __cppast(...)
#endif

namespace __cppast(script, is_browsable=true, desc=""a namespace test"") TestNs{

}

", compilation =>
                {
                    Assert.False(compilation.HasErrors);

                    //annotate attribute support on namespace
                    var ns = compilation.Namespaces[0];

                    Assert.True(ns.MetaAttributes.QueryArgumentAsBool("script", false));
                    Assert.False(!ns.MetaAttributes.QueryArgumentAsBool("is_browsable", false));
                    Assert.Equal("a namespace test", ns.MetaAttributes.QueryArgumentAsString("desc", ""));

                }
            );
        }

        [Fact]
        public void TestClassMetaAttribute()
        {

            ParseAssert(@"

#if !defined(__cppast)
#define __cppast(...)
#endif

class __cppast(script, is_browsable=true, desc=""a class"") TestClass
{
  public:
    __cppast(desc=""a member function"")
    __cppast(desc2=""a member function 2"")
    void TestMemberFunc();

    __cppast(desc=""a member field"")
    __cppast(desc2=""a member field 2"")
    int X;
};

", compilation =>
                {
                    Assert.False(compilation.HasErrors);

                    var cppClass = compilation.Classes[0];
                    Assert.True(cppClass.MetaAttributes.QueryArgumentAsBool("script", false));
                    Assert.False(!cppClass.MetaAttributes.QueryArgumentAsBool("is_browsable", false));
                    Assert.Equal("a class", cppClass.MetaAttributes.QueryArgumentAsString("desc", ""));

                    Assert.Equal(1, cppClass.Functions.Count);
                    Assert.Equal("a member function", cppClass.Functions[0].MetaAttributes.QueryArgumentAsString("desc", ""));
                    Assert.Equal("a member function 2", cppClass.Functions[0].MetaAttributes.QueryArgumentAsString("desc2", ""));

                    Assert.Equal(1, cppClass.Fields.Count);
                    Assert.Equal("a member field", cppClass.Fields[0].MetaAttributes.QueryArgumentAsString("desc", ""));
                    Assert.Equal("a member field 2", cppClass.Fields[0].MetaAttributes.QueryArgumentAsString("desc2", ""));

                }
            );
        }

        [Fact]
        public void TestTemplateMetaAttribute()
        {

            ParseAssert(@"

#if !defined(__cppast)
#define __cppast(...)
#endif

template <typename T>
class TestTemplateClass
{
  public:

    __cppast(desc=""a template member field"")
    T X;
};

using IntClass __cppast(desc=""a template class for int"") = TestTemplateClass<int>;
using DoubleClass __cppast(desc=""a template class for double"") = TestTemplateClass<double>;

typedef TestTemplateClass<float> __cppast(desc=""a template class for float"") FloatClass;
", compilation =>
                {
                    Assert.False(compilation.HasErrors);

                    var templateClass = compilation.Classes[0];
                    Assert.Equal("a template member field", templateClass.Fields[0].MetaAttributes.QueryArgumentAsString("desc", ""));

                    var intClass = compilation.Classes[1];
                    var doubleClass = compilation.Classes[2];
                    var floatClass = compilation.Classes[3];
                    Assert.Equal("a template class for int", intClass.MetaAttributes.QueryArgumentAsString("desc", ""));
                    Assert.Equal("a template class for double", doubleClass.MetaAttributes.QueryArgumentAsString("desc", ""));
                    Assert.Equal("a template class for float", floatClass.MetaAttributes.QueryArgumentAsString("desc", ""));
                }
            );
        }
    }
}