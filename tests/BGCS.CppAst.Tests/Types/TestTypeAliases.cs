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
                    Assert.False(compilation.HasErrors);

                    Assert.Equal(13, compilation.Typedefs.Count);

                    var primitives = new CppPrimitiveType[]
                    {
                        CppPrimitiveType.Void,

                        CppPrimitiveType.Bool,

                        CppPrimitiveType.WChar,

                        CppPrimitiveType.Char,
                        CppPrimitiveType.UnsignedChar,

                        CppPrimitiveType.Short,
                        CppPrimitiveType.UnsignedShort,

                        CppPrimitiveType.Int,
                        CppPrimitiveType.UnsignedInt,

                        CppPrimitiveType.LongLong,
                        CppPrimitiveType.UnsignedLongLong,

                        CppPrimitiveType.Float,
                        CppPrimitiveType.Double,
                    };


                    for (int i = 0; i < primitives.Length; i++)
                    {
                        var typedef = compilation.Typedefs[i];
                        var expectedType = primitives[i];
                        Assert.Equal(expectedType, typedef.ElementType);
                        Assert.Equal("Type_" + expectedType.ToString().Replace(" ", "_"), typedef.Name);
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
                    Assert.False(compilation.HasErrors);

                    Assert.Equal(1, compilation.Classes.Count);
                    Assert.Equal("MyStruct", compilation.Classes[0].Name);

                    var cppStruct = compilation.FindByName<CppClass>("MyStruct");
                    Assert.Equal(compilation.Classes[0], cppStruct);
                }
            );


            ParseAssert(@text,
                compilation =>
                {
                    Assert.False(compilation.HasErrors);

                    Assert.Equal(1, compilation.Classes.Count);
                    Assert.Equal(1, compilation.Typedefs.Count);
                    Assert.Equal("MyStruct", compilation.Classes[0].Name);
                    Assert.Equal("MyStruct", compilation.Typedefs[0].Name);
                },
                new CppParserOptions() { AutoSquashTypedef = false }
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
                    Assert.False(compilation.HasErrors);
                    Assert.Equal(2, compilation.Classes.Count);
                    Assert.Equal("MyStruct", compilation.Classes[0].Name);

                    var cppStruct = compilation.FindByName<CppClass>("MyStruct");
                    Assert.Equal(compilation.Classes[0], cppStruct);

                    Assert.Equal(1, compilation.Typedefs.Count);
                    Assert.Equal("MyStructInt", compilation.Typedefs[0].Name);

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
                    Assert.False(compilation.HasErrors);
                    Assert.Equal(1, compilation.Classes.Count);
                    Assert.Equal("MyStruct", compilation.Classes[0].Name);

                    var cppStruct = compilation.FindByName<CppClass>("MyStruct");
                    Assert.Equal(compilation.Classes[0], cppStruct);

                    Assert.Equal(1, compilation.Typedefs.Count);
                    Assert.Equal("MyStructT", compilation.Typedefs[0].Name);

                }
            );
        }
    }
}
