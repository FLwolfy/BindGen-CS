using BGCS.Core.Targeting;
using BGCS.CppAst.Extensions;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Types;
using BGCS.CppAst.Parsing;
using BGCS.CppAst.Targeting;
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
                    Assert.False(compilation.hasErrors);

                    Assert.Equal(3, compilation.functions.Count);

                    {
                        var cppFunction = compilation.functions[0];
                        Assert.Equal("function0", cppFunction.name);
                        Assert.Empty(cppFunction.parameters);
                        Assert.Equal("void", cppFunction.returnType.ToString());

                        var cppFunction1 = compilation.FindByName<CppFunction>("function0");
                        Assert.Equal(cppFunction, cppFunction1);
                    }

                    {
                        var cppFunction = compilation.functions[1];
                        Assert.Equal("function1", cppFunction.name);
                        Assert.Equal(2, cppFunction.parameters.Count);
                        Assert.Equal("a", cppFunction.parameters[0].name);
                        Assert.Equal(CppTypeKind.Primitive, cppFunction.parameters[0].type.typeKind);
                        Assert.Equal(CppPrimitiveKind.Int, ((CppPrimitiveType)cppFunction.parameters[0].type).kind);
                        Assert.Equal("b", cppFunction.parameters[1].name);
                        Assert.Equal(CppTypeKind.Primitive, cppFunction.parameters[1].type.typeKind);
                        Assert.Equal(CppPrimitiveKind.Float, ((CppPrimitiveType)cppFunction.parameters[1].type).kind);
                        Assert.Equal("int", cppFunction.returnType.ToString());

                        var cppFunction1 = compilation.FindByName<CppFunction>("function1");
                        Assert.Equal(cppFunction, cppFunction1);
                    }
                    {
                        var cppFunction = compilation.functions[2];
                        Assert.Equal("function2", cppFunction.name);
                        Assert.Single(cppFunction.parameters);
                        Assert.Equal(string.Empty, cppFunction.parameters[0].name);
                        Assert.Equal(CppTypeKind.Primitive, cppFunction.parameters[0].type.typeKind);
                        Assert.Equal(CppPrimitiveKind.Int, ((CppPrimitiveType)cppFunction.parameters[0].type).kind);
                        Assert.Equal("float", cppFunction.returnType.ToString());

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
                    Assert.False(compilation.hasErrors);

                    Assert.Equal(2, compilation.typedefs.Count);

                    {
                        var cppType = compilation.typedefs[0].elementType;
                        Assert.Equal(CppTypeKind.Pointer, cppType.typeKind);
                        var cppPointerType = (CppPointerType)cppType;
                        Assert.Equal(CppTypeKind.Function, cppPointerType.elementType.typeKind);
                        var cppFunctionType = (CppFunctionType)cppPointerType.elementType;
                        Assert.Equal(2, cppFunctionType.parameters.Count);

                        Assert.Equal("a", cppFunctionType.parameters[0].name);
                        Assert.Equal(CppPrimitiveType.@int, cppFunctionType.parameters[0].type);

                        Assert.Equal("b", cppFunctionType.parameters[1].name);
                        Assert.Equal(CppPrimitiveType.@float, cppFunctionType.parameters[1].type);
                    }

                    {
                        var cppType = compilation.typedefs[1].elementType;
                        Assert.Equal(CppTypeKind.Pointer, cppType.typeKind);
                        var cppPointerType = (CppPointerType)cppType;
                        Assert.Equal(CppTypeKind.Function, cppPointerType.elementType.typeKind);
                        var cppFunctionType = (CppFunctionType)cppPointerType.elementType;
                        Assert.Equal(2, cppFunctionType.parameters.Count);

                        Assert.Equal(string.Empty, cppFunctionType.parameters[0].name);
                        Assert.Equal(CppPrimitiveType.@int, cppFunctionType.parameters[0].type);

                        Assert.Equal(string.Empty, cppFunctionType.parameters[1].name);
                        Assert.Equal(CppPrimitiveType.@float, cppFunctionType.parameters[1].type);
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
                    Assert.False(compilation.hasErrors);

                    var cls = compilation.classes[0];
                    Assert.Equal(2, cls.fields.Count);

                    {
                        var cppType = cls.fields[0].type;
                        Assert.Equal(CppTypeKind.Pointer, cppType.typeKind);
                        var cppPointerType = (CppPointerType)cppType;
                        Assert.Equal(CppTypeKind.Function, cppPointerType.elementType.typeKind);
                        var cppFunctionType = (CppFunctionType)cppPointerType.elementType;
                        Assert.Equal(2, cppFunctionType.parameters.Count);

                        Assert.Equal("a", cppFunctionType.parameters[0].name);
                        Assert.Equal(CppPrimitiveType.@int, cppFunctionType.parameters[0].type);

                        Assert.Equal("b", cppFunctionType.parameters[1].name);
                        Assert.Equal(CppPrimitiveType.@float, cppFunctionType.parameters[1].type);
                    }

                    {
                        var cppType = cls.fields[1].type;
                        Assert.Equal(CppTypeKind.Pointer, cppType.typeKind);
                        var cppPointerType = (CppPointerType)cppType;
                        Assert.Equal(CppTypeKind.Function, cppPointerType.elementType.typeKind);
                        var cppFunctionType = (CppFunctionType)cppPointerType.elementType;
                        Assert.Equal(2, cppFunctionType.parameters.Count);

                        Assert.Equal(string.Empty, cppFunctionType.parameters[0].name);
                        Assert.Equal(CppPrimitiveType.@char, cppFunctionType.parameters[0].type);

                        Assert.Equal(string.Empty, cppFunctionType.parameters[1].name);
                        Assert.Equal(CppPrimitiveType.@int, cppFunctionType.parameters[1].type);
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
                    Assert.False(compilation.hasErrors);

                    var cls = compilation.classes[0];
                    Assert.Equal(2, cls.fields.Count);

                    {
                        var cppType = cls.fields[0].type;
                        Assert.Equal(CppTypeKind.Typedef, cppType.typeKind);
                    }

                    {
                        var cppType = cls.fields[1].type;
                        Assert.Equal(CppTypeKind.Typedef, cppType.typeKind);
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
                    Assert.False(compilation.hasErrors);

                    Assert.Equal(2, compilation.functions.Count);

                    {
                        var cppFunction = compilation.functions[0];
                        Assert.Single(cppFunction.attributes);
                        Assert.True(cppFunction.IsPublicExport());
                    }
                    {
                        var cppFunction = compilation.functions[1];
                        Assert.Empty(cppFunction.attributes);
                        Assert.True(cppFunction.IsPublicExport());
                    }
                },
                new CppParserOptions() { }
            );

            ParseAssert(text,
                compilation =>
                {
                    Assert.False(compilation.hasErrors);

                    Assert.Equal(2, compilation.functions.Count);

                    {
                        var cppFunction = compilation.functions[0];
                        Assert.Single(cppFunction.attributes);
                        Assert.True(cppFunction.IsPublicExport());
                    }
                    {
                        var cppFunction = compilation.functions[1];
                        Assert.Empty(cppFunction.attributes);
                        Assert.True(cppFunction.IsPublicExport());
                    }
                }, new CppParserOptions() { }.ConfigureForTarget(new ClangTargetResolver().Resolve(new(new NativeTargetId("windows-x86-msvc"))))
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
                    Assert.False(compilation.hasErrors);

                    Assert.Equal(3, compilation.functions.Count);

                    {
                        var cppFunction = compilation.functions[0];
                        Assert.Empty(cppFunction.parameters);
                        Assert.Equal("void", cppFunction.returnType.ToString());
                        Assert.Equal(CppFunctionFlags.None, cppFunction.flags & CppFunctionFlags.Variadic);
                    }

                    {
                        var cppFunction = compilation.functions[1];
                        Assert.Empty(cppFunction.parameters);
                        Assert.Equal("void", cppFunction.returnType.ToString());
                        Assert.Equal(CppFunctionFlags.Variadic, cppFunction.flags & CppFunctionFlags.Variadic);
                    }

                    {
                        var cppFunction = compilation.functions[2];
                        Assert.Single(cppFunction.parameters);
                        Assert.Equal(string.Empty, cppFunction.parameters[0].name);
                        Assert.Equal(CppTypeKind.Primitive, cppFunction.parameters[0].type.typeKind);
                        Assert.Equal(CppPrimitiveKind.Int, ((CppPrimitiveType)cppFunction.parameters[0].type).kind);
                        Assert.Equal("void", cppFunction.returnType.ToString());
                        Assert.Equal(CppFunctionFlags.Variadic, cppFunction.flags & CppFunctionFlags.Variadic);
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
                    Assert.False(compilation.hasErrors);

                    Assert.Single(compilation.functions);

                    {
                        var cppFunction = compilation.functions[0];
                        Assert.Single(cppFunction.parameters);
                        Assert.Equal("void", cppFunction.returnType.ToString());
                        Assert.True(cppFunction.isFunctionTemplate);
                        Assert.Single(cppFunction.templateParameters);
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
                    Assert.False(compilation.hasErrors);

                    Assert.Single(compilation.functions);

                    {
                        var cppFunction = compilation.functions[0];
                        Assert.Equal("function0", cppFunction.name);
                        Assert.Equal(3, cppFunction.parameters.Count);

                        Assert.IsType<CppPointerType>(cppFunction.parameters[2].type);
                        var pointerType = (CppPointerType)cppFunction.parameters[2].type;
                        Assert.IsType<CppFunctionType>(pointerType.elementType);
                        var functionType = (CppFunctionType)pointerType.elementType;
                        Assert.Equal(2, functionType.parameters.Count);
                        Assert.Equal("float", functionType.returnType.ToString());
                        Assert.Equal("void *", functionType.parameters[0].type.ToString());
                        Assert.Equal("double", functionType.parameters[1].type.ToString());


                        Assert.Equal("void", cppFunction.returnType.ToString());

                        var cppFunction1 = compilation.FindByName<CppFunction>("function0");
                        Assert.Equal(cppFunction, cppFunction1);
                    }
                }
            );
        }



    }
}
