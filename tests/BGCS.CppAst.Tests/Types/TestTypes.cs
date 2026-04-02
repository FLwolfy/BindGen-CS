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

using BGCS.CppAst.Extensions;
using System;
using System.Linq;

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
                    Assert.False(compilation.HasErrors);

                    Assert.Equal(5, compilation.Fields.Count);
                    Assert.Equal(2, compilation.Typedefs.Count);

                    var types = new CppType[]
                    {
                        new CppReferenceType(default, CppPrimitiveType.Int) ,
                        new CppQualifiedType(default, CppTypeQualifier.Const, CppPrimitiveType.Float),

                        new CppPointerType(default, CppPrimitiveType.Char),
                        new CppQualifiedType(default, CppTypeQualifier.Const, CppPrimitiveType.Int),
                        new CppArrayType(default, CppPrimitiveType.Int, 5),
                        new CppPointerType(default, new CppFunctionType(default, CppPrimitiveType.Void)
                        {
                            Parameters =
                            {
                                new CppParameter(default, CppPrimitiveType.Int, "a"),
                                new CppParameter(default, CppPrimitiveType.Float, "b"),
                            }
                        }) { SizeOf = IntPtr.Size },
                        new CppPointerType(default, new CppQualifiedType(default, CppTypeQualifier.Const, CppPrimitiveType.Float))
                    };

                    var canonicalTypes = compilation.Typedefs.Select(x => x.GetCanonicalType()).Concat(compilation.Fields.Select(x => x.Type.GetCanonicalType())).ToList();
                    Assert.Equal(types.Select(x => x.SizeOf), canonicalTypes.Select(x => x.SizeOf));
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
                    Assert.False(compilation.HasErrors);

                    Assert.Equal(2, compilation.Fields.Count);

                    var exposed = compilation.Fields[0].Type as CppClass;
                    Assert.Equal("TemplateStruct", exposed.Name);
                    Assert.Equal(2, exposed.TemplateParameters.Count);
                    Assert.Equal(CppTemplateArgumentKind.AsType, exposed.TemplateSpecializedArguments[0]?.ArgKind);
                    Assert.Equal(CppPrimitiveKind.Int, (exposed.TemplateSpecializedArguments[0]?.ArgAsType as CppPrimitiveType).Kind);
                    Assert.Equal("Struct2", (exposed.TemplateSpecializedArguments[1].ArgAsType as CppClass)?.Name);

                    var specialized = exposed.SpecializedTemplate;
                    Assert.Equal("TemplateStruct", specialized.Name);
                    Assert.Equal(2, specialized.Fields.Count);
                    Assert.Equal("field0", specialized.Fields[0].Name);
                    Assert.Equal("T", specialized.Fields[0].Type.GetDisplayName());
                    Assert.Equal("field1", specialized.Fields[1].Name);
                    Assert.Equal("U", specialized.Fields[1].Type.GetDisplayName());

                    var unexposed = compilation.Fields[1].Type as CppClass;
                    Assert.Equal("TemplateStruct", unexposed.Name);
                    Assert.Equal(2, unexposed.TemplateParameters.Count);
                    Assert.Equal(CppTemplateArgumentKind.AsType, unexposed.TemplateSpecializedArguments[0]?.ArgKind);
                    Assert.Equal(CppPrimitiveKind.Int, (exposed.TemplateSpecializedArguments[0]?.ArgAsType as CppPrimitiveType).Kind);
                    Assert.Equal("Struct2", (unexposed.TemplateSpecializedArguments[1].ArgAsType as CppClass)?.Name);

                    Assert.NotEqual(exposed.GetHashCode(), specialized.GetHashCode());
                    Assert.Equal(exposed.GetHashCode(), unexposed.GetHashCode());
                }
            );
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
                    Assert.False(compilation.HasErrors);

                    Assert.Equal(3, compilation.Classes.Count);

                    var baseTemplate = compilation.Classes[0];
                    var derived = compilation.Classes[1];
                    var baseClassSpecialized = compilation.Classes[2];

                    Assert.Equal("BaseTemplate", baseTemplate.Name);
                    Assert.Equal("Derived", derived.Name);
                    Assert.Equal("BaseTemplate", baseClassSpecialized.Name);

                    Assert.Equal(1, derived.BaseTypes.Count);
                    Assert.Equal(baseClassSpecialized, derived.BaseTypes[0].Type);

                    Assert.Equal(1, baseClassSpecialized.TemplateParameters.Count);

                    //Here change to argument as a template deduce instance, not as a Template Parameters~~
                    Assert.Equal(derived, baseClassSpecialized.TemplateSpecializedArguments[0].ArgAsType);
                    Assert.Equal(baseTemplate, baseClassSpecialized.SpecializedTemplate);
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
                    Assert.False(compilation.HasErrors);

                    Assert.Equal(3, compilation.Classes.Count);
                    Assert.Equal(1, compilation.Fields.Count);

                    var baseTemplate = compilation.Classes[0];
                    var fullSpecializedClass = compilation.Classes[1];
                    var partialSpecializedTemplate = compilation.Classes[2];

                    var field = compilation.Fields[0];
                    Assert.Equal(field.Name, "foobar");

                    Assert.Equal(baseTemplate.TemplateKind, CppTemplateKind.TemplateClass);
                    Assert.Equal(fullSpecializedClass.TemplateKind, CppTemplateKind.TemplateSpecializedClass);
                    Assert.Equal(partialSpecializedTemplate.TemplateKind, CppTemplateKind.PartialTemplateClass);

                    //Need be a specialized for partial template here
                    Assert.Equal(fullSpecializedClass.SpecializedTemplate, partialSpecializedTemplate);

                    //Need be a full specialized class for this field
                    Assert.Equal(field.Type, fullSpecializedClass);

                    Assert.Equal(partialSpecializedTemplate.TemplateSpecializedArguments.Count, 2);
                    //The first argument is integer now
                    Assert.Equal(partialSpecializedTemplate.TemplateSpecializedArguments[0].ArgString, "int");
                    //The second argument is not a specialized argument, we do not specialized a `B` template parameter here(partial specialized template)
                    Assert.Equal(partialSpecializedTemplate.TemplateSpecializedArguments[1].IsSpecializedArgument, false);

                    //The field use type is a full specialized type here~, so we can have two `int` template parmerater here
                    //It's a not template or partial template class, so we can instantiate it, see `foo<int, int> foobar;` before.
                    Assert.Equal(fullSpecializedClass.TemplateSpecializedArguments.Count, 2);
                    //The first argument is integer now
                    Assert.Equal(fullSpecializedClass.TemplateSpecializedArguments[0].ArgString, "int");
                    //The second argument is not a specialized argument
                    Assert.Equal(fullSpecializedClass.TemplateSpecializedArguments[1].ArgString, "int");
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
                    Assert.False(compilation.HasErrors);

                    var tmpClass1 = compilation.Namespaces[1].Fields[0];
                    var tmpClass2 = compilation.Namespaces[1].Fields[1];
                    var constDummyPointer = compilation.Namespaces[1].Fields[2];

                    var hoge = tmpClass1.Type.GetDisplayName();
                    var hoge2 = tmpClass2.Type.GetDisplayName();
                    var hoge3 = tmpClass2.Type.GetDisplayName();
                    Assert.Equal("TmpClass const *", tmpClass1.Type.GetDisplayName());
                    Assert.Equal("TmpClass volatile *", tmpClass2.Type.GetDisplayName());
                    Assert.Equal("unsigned int const * const", constDummyPointer.Type.GetDisplayName());
                }
            );
        }
    }
}
