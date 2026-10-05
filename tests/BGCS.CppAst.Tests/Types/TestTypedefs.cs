using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Types;
using BGCS.CppAst.Parsing;
using Xunit;
namespace BGCS.CppAst.Tests
{
    public class TestTypedefs : InlineTestBase
    {
        [Fact]
        public void TestSimple()
        {
            ParseAssert(@"
typedef void Type_void;

typedef bool Type_bool;

typedef wchar_t Type_wchar_t;

typedef char Type_char;
typedef unsigned char Type_unsigned_char;

typedef short Type_short;
typedef unsigned short Type_unsigned_short;

typedef int Type_int;
typedef unsigned int Type_unsigned_int;

typedef long long Type_long_long;
typedef unsigned long long Type_unsigned_long_long;

typedef float Type_float;
typedef double Type_double;
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
typedef struct {
    int field0;
} MyStruct;
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
    }
}