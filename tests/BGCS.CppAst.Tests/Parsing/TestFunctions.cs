using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    public class TestFunctions : InlineTestBase
    {
        [Fact]
        public void TestSimple()
        {
            ParseAssert(@"
void function0();
int function1(int a, float b);
float function2(int);
",
                compilation =>
                {
                    Assert.False(compilation.HasErrors);

                    Assert.Equal(3, compilation.Functions.Count);

                    {
                        var cppFunction = compilation.Functions[0];
                        Assert.Equal("function0", cppFunction.Name);
                        Assert.Equal(0, cppFunction.Parameters.Count);
                        Assert.Equal("void", cppFunction.ReturnType.ToString());

                        var cppFunction1 = compilation.FindByName<CppFunction>("function0");
                        Assert.Equal(cppFunction, cppFunction1);
                    }

                    {
                        var cppFunction = compilation.Functions[1];
                        Assert.Equal("function1", cppFunction.Name);
                        Assert.Equal(2, cppFunction.Parameters.Count);
                        Assert.Equal("a", cppFunction.Parameters[0].Name);
                        Assert.Equal(CppTypeKind.Primitive, cppFunction.Parameters[0].Type.TypeKind);
                        Assert.Equal(CppPrimitiveKind.Int, ((CppPrimitiveType)cppFunction.Parameters[0].Type).Kind);
                        Assert.Equal("b", cppFunction.Parameters[1].Name);
                        Assert.Equal(CppTypeKind.Primitive, cppFunction.Parameters[1].Type.TypeKind);
                        Assert.Equal(CppPrimitiveKind.Float, ((CppPrimitiveType)cppFunction.Parameters[1].Type).Kind);
                        Assert.Equal("int", cppFunction.ReturnType.ToString());

                        var cppFunction1 = compilation.FindByName<CppFunction>("function1");
                        Assert.Equal(cppFunction, cppFunction1);
                    }
                    {
                        var cppFunction = compilation.Functions[2];
                        Assert.Equal("function2", cppFunction.Name);
                        Assert.Equal(1, cppFunction.Parameters.Count);
                        Assert.Equal(string.Empty, cppFunction.Parameters[0].Name);
                        Assert.Equal(CppTypeKind.Primitive, cppFunction.Parameters[0].Type.TypeKind);
                        Assert.Equal(CppPrimitiveKind.Int, ((CppPrimitiveType)cppFunction.Parameters[0].Type).Kind);
                        Assert.Equal("float", cppFunction.ReturnType.ToString());

                        var cppFunction1 = compilation.FindByName<CppFunction>("function2");
                        Assert.Equal(cppFunction, cppFunction1);
                    }
                    {
                    }
                }
            );
        }


        [Fact]
        public void TestFunctionPrototype()
        {
            ParseAssert(@"
typedef void (*function0)(int a, float b);
typedef void (*function1)(int, float);
",
                compilation =>
                {
                    Assert.False(compilation.HasErrors);

                    Assert.Equal(2, compilation.Typedefs.Count);

                    {
                        var cppType = compilation.Typedefs[0].ElementType;
                        Assert.Equal(CppTypeKind.Pointer, cppType.TypeKind);
                        var cppPointerType = (CppPointerType)cppType;
                        Assert.Equal(CppTypeKind.Function, cppPointerType.ElementType.TypeKind);
                        var cppFunctionType = (CppFunctionType)cppPointerType.ElementType;
                        Assert.Equal(2, cppFunctionType.Parameters.Count);

                        Assert.Equal("a", cppFunctionType.Parameters[0].Name);
                        Assert.Equal(CppPrimitiveType.Int, cppFunctionType.Parameters[0].Type);

                        Assert.Equal("b", cppFunctionType.Parameters[1].Name);
                        Assert.Equal(CppPrimitiveType.Float, cppFunctionType.Parameters[1].Type);
                    }

                    {
                        var cppType = compilation.Typedefs[1].ElementType;
                        Assert.Equal(CppTypeKind.Pointer, cppType.TypeKind);
                        var cppPointerType = (CppPointerType)cppType;
                        Assert.Equal(CppTypeKind.Function, cppPointerType.ElementType.TypeKind);
                        var cppFunctionType = (CppFunctionType)cppPointerType.ElementType;
                        Assert.Equal(2, cppFunctionType.Parameters.Count);

                        Assert.Equal(string.Empty, cppFunctionType.Parameters[0].Name);
                        Assert.Equal(CppPrimitiveType.Int, cppFunctionType.Parameters[0].Type);

                        Assert.Equal(string.Empty, cppFunctionType.Parameters[1].Name);
                        Assert.Equal(CppPrimitiveType.Float, cppFunctionType.Parameters[1].Type);
                    }

                }
            );
        }

        [Fact]
        public void TestFunctionFields()
        {
            ParseAssert(@"
typedef struct struct0 {
    void (*function0)(int a, float b);
    void (*function1)(char, int);
} struct0;
",
                compilation =>
                {
                    Assert.False(compilation.HasErrors);

                    var cls = compilation.Classes[0];
                    Assert.Equal(2, cls.Fields.Count);

                    {
                        var cppType = cls.Fields[0].Type;
                        Assert.Equal(CppTypeKind.Pointer, cppType.TypeKind);
                        var cppPointerType = (CppPointerType)cppType;
                        Assert.Equal(CppTypeKind.Function, cppPointerType.ElementType.TypeKind);
                        var cppFunctionType = (CppFunctionType)cppPointerType.ElementType;
                        Assert.Equal(2, cppFunctionType.Parameters.Count);

                        Assert.Equal("a", cppFunctionType.Parameters[0].Name);
                        Assert.Equal(CppPrimitiveType.Int, cppFunctionType.Parameters[0].Type);

                        Assert.Equal("b", cppFunctionType.Parameters[1].Name);
                        Assert.Equal(CppPrimitiveType.Float, cppFunctionType.Parameters[1].Type);
                    }

                    {
                        var cppType = cls.Fields[1].Type;
                        Assert.Equal(CppTypeKind.Pointer, cppType.TypeKind);
                        var cppPointerType = (CppPointerType)cppType;
                        Assert.Equal(CppTypeKind.Function, cppPointerType.ElementType.TypeKind);
                        var cppFunctionType = (CppFunctionType)cppPointerType.ElementType;
                        Assert.Equal(2, cppFunctionType.Parameters.Count);

                        Assert.Equal(string.Empty, cppFunctionType.Parameters[0].Name);
                        Assert.Equal(CppPrimitiveType.Char, cppFunctionType.Parameters[0].Type);

                        Assert.Equal(string.Empty, cppFunctionType.Parameters[1].Name);
                        Assert.Equal(CppPrimitiveType.Int, cppFunctionType.Parameters[1].Type);
                    }

                }
            );
        }


        [Fact]
        public void TestFunctionTypedefFields()
        {
            ParseAssert(@"
typedef struct struct0 struct0;
typedef void (*function0_t)(int a, float b);
typedef void (*function1_t)(char, int);
struct struct0
{
    function0_t function0;
    function1_t function1;
};
",
                compilation =>
                {
                    Assert.False(compilation.HasErrors);

                    var cls = compilation.Classes[0];
                    Assert.Equal(2, cls.Fields.Count);

                    {
                        var cppType = cls.Fields[0].Type;
                        Assert.Equal(CppTypeKind.Typedef, cppType.TypeKind);
                    }

                    {
                        var cppType = cls.Fields[1].Type;
                        Assert.Equal(CppTypeKind.Typedef, cppType.TypeKind);
                    }
                }
            );
        }

        [Fact]
        public void TestFunctionExport()
        {
            var text = @"
#ifdef WIN32
#define EXPORT_API __declspec(dllexport)
#else
#define EXPORT_API __attribute__((visibility(""default"")))
#endif
EXPORT_API int function0();
int function1();
";

            ParseAssert(text,
                compilation =>
                {
                    Assert.False(compilation.HasErrors);

                    Assert.Equal(2, compilation.Functions.Count);

                    {
                        var cppFunction = compilation.Functions[0];
                        Assert.Equal(1, cppFunction.Attributes.Count);
                        Assert.True(cppFunction.IsPublicExport());
                    }
                    {
                        var cppFunction = compilation.Functions[1];
                        Assert.Equal(0, cppFunction.Attributes.Count);
                        Assert.True(cppFunction.IsPublicExport());
                    }
                },
                new CppParserOptions() { }
            );

            ParseAssert(text,
                compilation =>
                {
                    Assert.False(compilation.HasErrors);

                    Assert.Equal(2, compilation.Functions.Count);

                    {
                        var cppFunction = compilation.Functions[0];
                        Assert.Equal(1, cppFunction.Attributes.Count);
                        Assert.True(cppFunction.IsPublicExport());
                    }
                    {
                        var cppFunction = compilation.Functions[1];
                        Assert.Equal(0, cppFunction.Attributes.Count);
                        Assert.True(cppFunction.IsPublicExport());
                    }
                }, new CppParserOptions() { }.ConfigureForWindowsMsvc()
            );
        }

        [Fact]
        public void TestFunctionVariadic()
        {
            ParseAssert(@"
void function0();
void function1(...);
void function2(int, ...);
",
                compilation =>
                {
                    Assert.False(compilation.HasErrors);

                    Assert.Equal(3, compilation.Functions.Count);

                    {
                        var cppFunction = compilation.Functions[0];
                        Assert.Equal(0, cppFunction.Parameters.Count);
                        Assert.Equal("void", cppFunction.ReturnType.ToString());
                        Assert.Equal(CppFunctionFlags.None, cppFunction.Flags & CppFunctionFlags.Variadic);
                    }

                    {
                        var cppFunction = compilation.Functions[1];
                        Assert.Equal(0, cppFunction.Parameters.Count);
                        Assert.Equal("void", cppFunction.ReturnType.ToString());
                        Assert.Equal(CppFunctionFlags.Variadic, cppFunction.Flags & CppFunctionFlags.Variadic);
                    }

                    {
                        var cppFunction = compilation.Functions[2];
                        Assert.Equal(1, cppFunction.Parameters.Count);
                        Assert.Equal(string.Empty, cppFunction.Parameters[0].Name);
                        Assert.Equal(CppTypeKind.Primitive, cppFunction.Parameters[0].Type.TypeKind);
                        Assert.Equal(CppPrimitiveKind.Int, ((CppPrimitiveType)cppFunction.Parameters[0].Type).Kind);
                        Assert.Equal("void", cppFunction.ReturnType.ToString());
                        Assert.Equal(CppFunctionFlags.Variadic, cppFunction.Flags & CppFunctionFlags.Variadic);
                    }
                }
            );
        }



        [Fact]
        public void TestFunctionTemplate()
        {
            ParseAssert(@"
template<class T>
void function0(T t);
",
                compilation =>
                {
                    Assert.False(compilation.HasErrors);

                    Assert.Equal(1, compilation.Functions.Count);

                    {
                        var cppFunction = compilation.Functions[0];
                        Assert.Equal(1, cppFunction.Parameters.Count);
                        Assert.Equal("void", cppFunction.ReturnType.ToString());
                        Assert.Equal(cppFunction.IsFunctionTemplate, true);
                        Assert.Equal(cppFunction.TemplateParameters.Count, 1);
                    }

                }
            );
        }


        [Fact]
        public void TestFunctionPointersByParam()
        {
            ParseAssert(@"
void function0(int a, int b, float (*callback)(void*, double));
",
                compilation =>
                {
                    Assert.False(compilation.HasErrors);

                    Assert.Equal(1, compilation.Functions.Count);

                    {
                        var cppFunction = compilation.Functions[0];
                        Assert.Equal("function0", cppFunction.Name);
                        Assert.Equal(3, cppFunction.Parameters.Count);

                        Assert.IsType<CppPointerType>(cppFunction.Parameters[2].Type);
                        var pointerType = (CppPointerType)cppFunction.Parameters[2].Type;
                        Assert.IsType<CppFunctionType>(pointerType.ElementType);
                        var functionType = (CppFunctionType)pointerType.ElementType;
                        Assert.Equal(2, functionType.Parameters.Count);
                        Assert.Equal("float", functionType.ReturnType.ToString());
                        Assert.Equal("void *", functionType.Parameters[0].Type.ToString());
                        Assert.Equal("double", functionType.Parameters[1].Type.ToString());


                        Assert.Equal("void", cppFunction.ReturnType.ToString());

                        var cppFunction1 = compilation.FindByName<CppFunction>("function0");
                        Assert.Equal(cppFunction, cppFunction1);
                    }
                }
            );
        }



    }
}