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
typedef struct {
    int field0;
} MyStruct;
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
    }
}