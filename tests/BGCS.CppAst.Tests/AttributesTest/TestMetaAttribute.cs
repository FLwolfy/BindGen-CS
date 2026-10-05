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
                    Assert.False(compilation.hasErrors);

                    //annotate attribute support on namespace
                    var ns = compilation.namespaces[0];

                    Assert.True(ns.metaAttributes.QueryArgumentAsBool("script", false));
                    Assert.False(!ns.metaAttributes.QueryArgumentAsBool("is_browsable", false));
                    Assert.Equal("a namespace test", ns.metaAttributes.QueryArgumentAsString("desc", ""));

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
                    Assert.False(compilation.hasErrors);

                    var cppClass = compilation.classes[0];
                    Assert.True(cppClass.metaAttributes.QueryArgumentAsBool("script", false));
                    Assert.False(!cppClass.metaAttributes.QueryArgumentAsBool("is_browsable", false));
                    Assert.Equal("a class", cppClass.metaAttributes.QueryArgumentAsString("desc", ""));

                    Assert.Single(cppClass.functions);
                    Assert.Equal("a member function", cppClass.functions[0].metaAttributes.QueryArgumentAsString("desc", ""));
                    Assert.Equal("a member function 2", cppClass.functions[0].metaAttributes.QueryArgumentAsString("desc2", ""));

                    Assert.Single(cppClass.fields);
                    Assert.Equal("a member field", cppClass.fields[0].metaAttributes.QueryArgumentAsString("desc", ""));
                    Assert.Equal("a member field 2", cppClass.fields[0].metaAttributes.QueryArgumentAsString("desc2", ""));

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
                    Assert.False(compilation.hasErrors);

                    var templateClass = compilation.classes[0];
                    Assert.Equal("a template member field", templateClass.fields[0].metaAttributes.QueryArgumentAsString("desc", ""));

                    var intClass = compilation.classes[1];
                    var doubleClass = compilation.classes[2];
                    var floatClass = compilation.classes[3];
                    Assert.Equal("a template class for int", intClass.metaAttributes.QueryArgumentAsString("desc", ""));
                    Assert.Equal("a template class for double", doubleClass.metaAttributes.QueryArgumentAsString("desc", ""));
                    Assert.Equal("a template class for float", floatClass.metaAttributes.QueryArgumentAsString("desc", ""));
                }
            );
        }
    }
}