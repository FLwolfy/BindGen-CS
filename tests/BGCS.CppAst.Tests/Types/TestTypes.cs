using System;
using System.Linq;
using BGCS.CppAst.Extensions;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Templates;
using BGCS.CppAst.Model.Types;
using Xunit;
// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace BGCS.CppAst.Tests
{
    public class TestTypes : InlineTestBase
    {
        [Fact]
        public void TestSimple()
        {
            ParseAssert(@"
typedef int& t0; // reference type
typedef const float t1;
char* f0; // pointer type
const int f1 = 5; // qualified type
int f2[5]; // array type
void (*f3)(int arg1, float arg2); // function type
t1* f4;
",
                compilation =>
                {
                    Assert.False(compilation.hasErrors);

                    Assert.Equal(5, compilation.fields.Count);
                    Assert.Equal(2, compilation.typedefs.Count);

                    var types = new CppType[]
                    {
                        new CppReferenceType(default, CppPrimitiveType.@int) ,
                        new CppQualifiedType(default, CppTypeQualifier.Const, CppPrimitiveType.@float),

                        new CppPointerType(default, CppPrimitiveType.@char, System.IntPtr.Size),
                        new CppQualifiedType(default, CppTypeQualifier.Const, CppPrimitiveType.@int),
                        new CppArrayType(default, CppPrimitiveType.@int, 5),
                        new CppPointerType(default, new CppFunctionType(default, CppPrimitiveType.@void)
                        {
                            parameters =
                            {
                                new CppParameter(default, CppPrimitiveType.@int, "a"),
                                new CppParameter(default, CppPrimitiveType.@float, "b"),
                            }
                        }, System.IntPtr.Size) { sizeOf = IntPtr.Size },
                        new CppPointerType(default, new CppQualifiedType(default, CppTypeQualifier.Const, CppPrimitiveType.@float), System.IntPtr.Size)
                    };

                    var canonicalTypes = compilation.typedefs.Select(x => x.GetCanonicalType()).Concat(compilation.fields.Select(x => x.type.GetCanonicalType())).ToList();
                    Assert.Equal(types.Select(x => x.sizeOf), canonicalTypes.Select(x => x.sizeOf));
                }
            );
        }

        [Fact]
        public void TestTemplateParameters()
        {
            ParseAssert(@"
template <typename T, typename U>
struct TemplateStruct
{
    T field0;
    U field1;
};

struct Struct2
{
};

::TemplateStruct<int, Struct2> exposed;
TemplateStruct<int, Struct2> unexposed;
",
                compilation =>
                {
                    Assert.False(compilation.hasErrors);

                    Assert.Equal(2, compilation.fields.Count);

                    var exposed = Assert.IsType<CppClass>(compilation.fields[0].type);
                    Assert.Equal("TemplateStruct", exposed.name);
                    Assert.Equal(2, exposed.templateParameters.Count);
                    Assert.Equal(CppTemplateArgumentKind.AsType, exposed.templateSpecializedArguments[0]?.argKind);
                    var exposedPrimitive = Assert.IsType<CppPrimitiveType>(exposed.templateSpecializedArguments[0]?.argAsType);
                    Assert.Equal(CppPrimitiveKind.Int, exposedPrimitive.kind);
                    Assert.Equal("Struct2", (exposed.templateSpecializedArguments[1].argAsType as CppClass)?.name);

                    var specialized = Assert.IsType<CppClass>(exposed.specializedTemplate);
                    Assert.Equal("TemplateStruct", specialized.name);
                    Assert.Equal(2, specialized.fields.Count);
                    Assert.Equal("field0", specialized.fields[0].name);
                    Assert.Equal("T", specialized.fields[0].type.GetDisplayName());
                    Assert.Equal("field1", specialized.fields[1].name);
                    Assert.Equal("U", specialized.fields[1].type.GetDisplayName());

                    var unexposed = Assert.IsType<CppClass>(compilation.fields[1].type);
                    Assert.Equal("TemplateStruct", unexposed.name);
                    Assert.Equal(2, unexposed.templateParameters.Count);
                    Assert.Equal(CppTemplateArgumentKind.AsType, unexposed.templateSpecializedArguments[0]?.argKind);
                    Assert.Equal(CppPrimitiveKind.Int, exposedPrimitive.kind);
                    Assert.Equal("Struct2", (unexposed.templateSpecializedArguments[1].argAsType as CppClass)?.name);

                    Assert.NotEqual(exposed.GetHashCode(), specialized.GetHashCode());
                    Assert.Equal(exposed.GetHashCode(), unexposed.GetHashCode());
                }
            );
        }

        [Fact]
        public void TemplateTemplateParameter_RetainsNestedSignature()
        {
            ParseAssert(@"
template <template <typename Item> class Container, typename Value>
struct GenericHolder { Container<Value>* value; };
template <typename T> struct Box { T value; };
GenericHolder<Box, int>* holder;
", compilation =>
            {
                Assert.False(compilation.hasErrors);
                CppClass generic = Assert.Single(compilation.classes, candidate =>
                    candidate.name == "GenericHolder" && candidate.specializedTemplate == null);
                CppTemplateParameterTemplate parameter = Assert.IsType<CppTemplateParameterTemplate>(generic.templateParameters[0]);
                Assert.Equal("Container", parameter.name);
                Assert.Equal(CppTypeKind.TemplateParameterTemplate, parameter.typeKind);
                Assert.Equal("Item", Assert.IsType<CppTemplateParameterType>(Assert.Single(parameter.parameters)).name);
            });
        }

        [Fact]
        public void IncompleteArray_UsesUnboundExtentInsteadOfNegativeStorageSize()
        {
            ParseAssert("extern int values[];", compilation =>
            {
                Assert.False(compilation.hasErrors);
                CppArrayType array = Assert.IsType<CppArrayType>(Assert.Single(compilation.fields).type);
                Assert.Equal(0, array.size);
                Assert.Equal(0, array.sizeOf);
            });
        }

        [Fact]
        public void DependentArray_DoesNotPretendToHaveAConcreteExtent()
        {
            ParseAssert("template <int N> struct Buffer { int values[N]; };", compilation =>
            {
                Assert.False(compilation.hasErrors);
                CppClass buffer = Assert.Single(compilation.classes);
                CppArrayType array = Assert.IsType<CppArrayType>(Assert.Single(buffer.fields).type);
                Assert.Equal(0, array.size);
                Assert.Equal(0, array.sizeOf);
                Assert.Contains(compilation.diagnostics.messages,
                    message => message.text.Contains("Dependent sized arrays", StringComparison.Ordinal));
            });
        }

        [Fact]
        public void TestTemplateInheritance()
        {
            ParseAssert(@"
template <typename T>
class BaseTemplate
{
};

class Derived : public ::BaseTemplate<::Derived>
{
};
",
                compilation =>
                {
                    Assert.False(compilation.hasErrors);

                    Assert.Equal(3, compilation.classes.Count);

                    var baseTemplate = compilation.classes[0];
                    var derived = compilation.classes[1];
                    var baseClassSpecialized = compilation.classes[2];

                    Assert.Equal("BaseTemplate", baseTemplate.name);
                    Assert.Equal("Derived", derived.name);
                    Assert.Equal("BaseTemplate", baseClassSpecialized.name);

                    Assert.Single(derived.baseTypes);
                    Assert.Equal(baseClassSpecialized, derived.baseTypes[0].type);

                    Assert.Single(baseClassSpecialized.templateParameters);

                    //Here change to argument as a template deduce instance, not as a Template Parameters~~
                    Assert.Equal(derived, baseClassSpecialized.templateSpecializedArguments[0].argAsType);
                    Assert.Equal(baseTemplate, baseClassSpecialized.specializedTemplate);
                }
            );
        }

        [Fact]
        public void TestTemplatePartialSpecialization()
        {
            ParseAssert(@"
template<typename A, typename B>
struct foo {};

template<typename B>
struct foo<int, B> {};

foo<int, int> foobar;
",
                compilation =>
                {
                    Assert.False(compilation.hasErrors);

                    Assert.Equal(3, compilation.classes.Count);
                    Assert.Single(compilation.fields);

                    var baseTemplate = compilation.classes[0];
                    var fullSpecializedClass = compilation.classes[1];
                    var partialSpecializedTemplate = compilation.classes[2];

                    var field = compilation.fields[0];
                    Assert.Equal("foobar", field.name);

                    Assert.Equal(CppTemplateKind.TemplateClass, baseTemplate.templateKind);
                    Assert.Equal(CppTemplateKind.TemplateSpecializedClass, fullSpecializedClass.templateKind);
                    Assert.Equal(CppTemplateKind.PartialTemplateClass, partialSpecializedTemplate.templateKind);

                    //Need be a specialized for partial template here
                    Assert.Equal(fullSpecializedClass.specializedTemplate, partialSpecializedTemplate);

                    //Need be a full specialized class for this field
                    Assert.Equal(field.type, fullSpecializedClass);

                    Assert.Equal(2, partialSpecializedTemplate.templateSpecializedArguments.Count);
                    //The first argument is integer now
                    Assert.Equal("int", partialSpecializedTemplate.templateSpecializedArguments[0].argString);
                    //The second argument is not a specialized argument, we do not specialized a `B` template parameter here(partial specialized template)
                    Assert.False(partialSpecializedTemplate.templateSpecializedArguments[1].isSpecializedArgument);

                    //The field use type is a full specialized type here~, so we can have two `int` template parmerater here
                    //It's a not template or partial template class, so we can instantiate it, see `foo<int, int> foobar;` before.
                    Assert.Equal(2, fullSpecializedClass.templateSpecializedArguments.Count);
                    //The first argument is integer now
                    Assert.Equal("int", fullSpecializedClass.templateSpecializedArguments[0].argString);
                    //The second argument is not a specialized argument
                    Assert.Equal("int", fullSpecializedClass.templateSpecializedArguments[1].argString);
                }
            );
        }

        [Fact]
        public void TestClassPrototype()
        {
            ParseAssert(@"
namespace ns1 {
class TmpClass;
}

namespace ns2 {
const ns1::TmpClass* tmpClass1;
volatile ns1::TmpClass* tmpClass2;
const unsigned int * const dummy_pu32 = (const unsigned int * const)0x12345678;
}

namespace ns1 {
class TmpClass {
};
}
",
                compilation =>
                {
                    Assert.False(compilation.hasErrors);

                    var tmpClass1 = compilation.namespaces[1].fields[0];
                    var tmpClass2 = compilation.namespaces[1].fields[1];
                    var constDummyPointer = compilation.namespaces[1].fields[2];

                    var hoge = tmpClass1.type.GetDisplayName();
                    var hoge2 = tmpClass2.type.GetDisplayName();
                    var hoge3 = tmpClass2.type.GetDisplayName();
                    Assert.Equal("TmpClass const *", tmpClass1.type.GetDisplayName());
                    Assert.Equal("TmpClass volatile *", tmpClass2.type.GetDisplayName());
                    Assert.Equal("unsigned int const * const", constDummyPointer.type.GetDisplayName());
                }
            );
        }
    }
}
