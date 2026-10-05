using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Types;
using BGCS.CppAst.Parsing;
using Xunit;
namespace BGCS.CppAst.Tests
{
    public class TestTypeAliases : InlineTestBase
    {
        [Fact]
        public void TestSimple()
        {
            ParseAssert(@"
using Type_void = void;

using Type_bool = bool;

using Type_wchar_t = wchar_t ;

using Type_char = char;
using Type_unsigned_char = unsigned char;

using Type_short = short;
using Type_unsigned_short = unsigned short ;

using Type_int = int;
using Type_unsigned_int = unsigned int ;

using Type_long_long = long long;
using Type_unsigned_long_long = unsigned long long ;

using Type_float = float;
using Type_double = double;
",
                compilation =>
                {
                    Assert.False(compilation.hasErrors);

                    Assert.Equal(13, compilation.typedefs.Count);

                    var primitives = new CppPrimitiveType[]
                    {
                        CppPrimitiveType.@void,

                        CppPrimitiveType.@bool,

                        CppPrimitiveType.wChar,

                        CppPrimitiveType.@char,
                        CppPrimitiveType.unsignedChar,

                        CppPrimitiveType.@short,
                        CppPrimitiveType.unsignedShort,

                        CppPrimitiveType.@int,
                        CppPrimitiveType.unsignedInt,

                        CppPrimitiveType.longLong,
                        CppPrimitiveType.unsignedLongLong,

                        CppPrimitiveType.@float,
                        CppPrimitiveType.@double,
                    };


                    for (int i = 0; i < primitives.Length; i++)
                    {
                        var typedef = compilation.typedefs[i];
                        var expectedType = primitives[i];
                        Assert.Equal(expectedType, typedef.elementType);
                        Assert.Equal("Type_" + expectedType.ToString().Replace(" ", "_"), typedef.name);
                    }
                }
            );
        }

        [Fact]
        public void TestSquash()
        {
            var text = @"
// Test typedef collapsing
using MyStruct = struct {
    int field0;
};
";

            ParseAssert(text,
                compilation =>
                {
                    Assert.False(compilation.hasErrors);

                    Assert.Single(compilation.classes);
                    Assert.Equal("MyStruct", compilation.classes[0].name);

                    var cppStruct = compilation.FindByName<CppClass>("MyStruct");
                    Assert.Equal(compilation.classes[0], cppStruct);
                }
            );


            ParseAssert(@text,
                compilation =>
                {
                    Assert.False(compilation.hasErrors);

                    Assert.Single(compilation.classes);
                    Assert.Single(compilation.typedefs);
                    Assert.Equal("MyStruct", compilation.classes[0].name);
                    Assert.Equal("MyStruct", compilation.typedefs[0].name);
                },
                new CppParserOptions() { autoSquashTypedef = false }
            );
        }

        [Fact]
        public void TestTemplate()
        {
            var text = @"
// Test using Template
template <typename T>
struct MyStruct;

using MyStructInt = MyStruct<int>;
";

            ParseAssert(text,
                compilation =>
                {
                    Assert.False(compilation.hasErrors);
                    Assert.Equal(2, compilation.classes.Count);
                    Assert.Equal("MyStruct", compilation.classes[0].name);

                    var cppStruct = compilation.FindByName<CppClass>("MyStruct");
                    Assert.Equal(compilation.classes[0], cppStruct);

                    Assert.Single(compilation.typedefs);
                    Assert.Equal("MyStructInt", compilation.typedefs[0].name);

                }
            );
        }

        [Fact]
        public void TestTemplateComplex()
        {
            var text = @"
// Test using Template
template <typename T>
struct MyStruct;

template<typename T1> using MyStructT = MyStruct<T1>;
";

            ParseAssert(text,
                compilation =>
                {
                    Assert.False(compilation.hasErrors);
                    Assert.Single(compilation.classes);
                    Assert.Equal("MyStruct", compilation.classes[0].name);

                    var cppStruct = compilation.FindByName<CppClass>("MyStruct");
                    Assert.Equal(compilation.classes[0], cppStruct);

                    Assert.Single(compilation.typedefs);
                    Assert.Equal("MyStructT", compilation.typedefs[0].name);

                }
            );
        }
    }
}
