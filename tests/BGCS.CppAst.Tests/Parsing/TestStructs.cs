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
    public class TestStructs : InlineTestBase
    {
        [Fact]
        public void TestSimple()
        {
            ParseAssert(@"
struct Struct0
{
};

struct Struct1 : Struct0
{
};

struct Struct2
{
    int field0;
};

struct Struct3
{
private:
    int field0;
public:
    float field1;
};
",
                compilation =>
                {
                    Assert.False(compilation.HasErrors);

                    Assert.Equal(4, compilation.Classes.Count);

                    {
                        var cppStruct = compilation.Classes[0];
                        Assert.Equal("Struct0", cppStruct.Name);
                        Assert.Equal(0, cppStruct.Fields.Count);
                        Assert.Equal(sizeof(byte), cppStruct.SizeOf);
                        Assert.Equal(1, cppStruct.AlignOf);
                    }

                    {
                        var cppStruct = compilation.Classes[1];
                        Assert.Equal("Struct1", cppStruct.Name);
                        Assert.Equal(0, cppStruct.Fields.Count);
                        Assert.Equal(1, cppStruct.BaseTypes.Count);
                        Assert.True(cppStruct.BaseTypes[0].Type is CppClass);
                        Assert.True(ReferenceEquals(compilation.Classes[0], cppStruct.BaseTypes[0].Type));
                        Assert.Equal(sizeof(byte), cppStruct.SizeOf);
                        Assert.Equal(1, cppStruct.AlignOf);
                    }

                    {
                        var cppStruct = compilation.Classes[2];
                        Assert.Equal("Struct2", cppStruct.Name);
                        Assert.Equal(1, cppStruct.Fields.Count);
                        Assert.Equal("field0", cppStruct.Fields[0].Name);
                        Assert.Equal(CppTypeKind.Primitive, cppStruct.Fields[0].Type.TypeKind);
                        Assert.Equal(CppPrimitiveKind.Int, ((CppPrimitiveType)cppStruct.Fields[0].Type).Kind);
                        Assert.Equal(sizeof(int), cppStruct.SizeOf);
                        Assert.Equal(4, cppStruct.AlignOf);
                    }

                    {
                        var cppStruct = compilation.Classes[3];
                        Assert.Equal(2, cppStruct.Fields.Count);
                        Assert.Equal("field0", cppStruct.Fields[0].Name);
                        Assert.Equal(CppTypeKind.Primitive, cppStruct.Fields[0].Type.TypeKind);
                        Assert.Equal(CppPrimitiveKind.Int, ((CppPrimitiveType)cppStruct.Fields[0].Type).Kind);
                        Assert.Equal(CppVisibility.Private, cppStruct.Fields[0].Visibility);

                        Assert.Equal("field1", cppStruct.Fields[1].Name);
                        Assert.Equal(CppTypeKind.Primitive, cppStruct.Fields[1].Type.TypeKind);
                        Assert.Equal(CppPrimitiveKind.Float, ((CppPrimitiveType)cppStruct.Fields[1].Type).Kind);
                        Assert.Equal(CppVisibility.Public, cppStruct.Fields[1].Visibility);
                        Assert.Equal(sizeof(int), cppStruct.Fields[1].Offset);
                        Assert.Equal(sizeof(int) + sizeof(float), cppStruct.SizeOf);
                        Assert.Equal(4, cppStruct.AlignOf);
                    }
                }
            );
        }


        [Fact]
        public void TestAnonymous()
        {
            ParseAssert(@"
struct
{
    int a;
    int b;
} c;
",
                compilation =>
                {
                    Assert.False(compilation.HasErrors);

                    Assert.Equal(1, compilation.Classes.Count);

                    {
                        var cppStruct = compilation.Classes[0];
                        Assert.Equal(string.Empty, cppStruct.Name);
                        Assert.Equal(2, cppStruct.Fields.Count);
                        Assert.Equal(sizeof(int), cppStruct.Fields[1].Offset);
                        Assert.Equal(sizeof(int) + sizeof(int), cppStruct.SizeOf);
                        Assert.Equal(4, cppStruct.AlignOf);
                    }
                }
            );
        }


        [Fact]
        public void TestAnonymousUnion()
        {
            ParseAssert(@"
struct HelloWorld
{
    int a;
    union {
        int c;
        int d;
    };
    int b;
    union {
        int e;
        int f;
    };
};
",
                compilation =>
                {
                    Assert.False(compilation.HasErrors);

                    Assert.Equal(1, compilation.Classes.Count);

                    {
                        var cppStruct = compilation.Classes[0];
                        Assert.Equal(4, cppStruct.Fields.Count);

                        for (int i = 0; i < 4; i++)
                        {
                            Assert.Equal(i * 4, cppStruct.Fields[i].Offset);
                            Assert.Equal(4, cppStruct.Fields[i].Type.SizeOf);
                        }

                        // Check first union
                        Assert.Equal(string.Empty, cppStruct.Fields[1].Name);
                        Assert.IsType<CppClass>(cppStruct.Fields[1].Type);
                        var cppUnion = ((CppClass)cppStruct.Fields[1].Type);
                        Assert.Equal(CppClassKind.Union, ((CppClass)cppStruct.Fields[1].Type).ClassKind);
                        Assert.Equal(2, cppUnion.Fields.Count);

                        // Check 2nd union
                        Assert.Equal(string.Empty, cppStruct.Fields[3].Name);
                        Assert.IsType<CppClass>(cppStruct.Fields[3].Type);
                        cppUnion = ((CppClass)cppStruct.Fields[3].Type);
                        Assert.Equal(CppClassKind.Union, ((CppClass)cppStruct.Fields[3].Type).ClassKind);
                        Assert.Equal(2, cppUnion.Fields.Count);
                    }
                }
            );
        }

        [Fact]
        public void TestAnonymousUnionWithField()
        {
            ParseAssert(@"
struct HelloWorld
{
    int a;
    union {
        int c;
        int d;
    } e;
};
",
                compilation =>
                {
                    Assert.False(compilation.HasErrors);

                    Assert.Equal(1, compilation.Classes.Count);

                    {
                        var cppStruct = compilation.Classes[0];

                        // Only one union
                        Assert.Equal(1, cppStruct.Classes.Count);

                        // Only 2 fields
                        Assert.Equal(2, cppStruct.Fields.Count);

                        // Check the union
                        Assert.Equal("e", cppStruct.Fields[1].Name);
                        Assert.IsType<CppClass>(cppStruct.Fields[1].Type);
                        var cppUnion = ((CppClass)cppStruct.Fields[1].Type);
                        Assert.Equal(CppClassKind.Union, ((CppClass)cppStruct.Fields[1].Type).ClassKind);
                        Assert.Equal(2, cppUnion.Fields.Count);
                    }
                }
            );
        }

        [Fact]
        public void TestAnonymousUnionWithField2()
        {
            ParseAssert(@"
struct HelloWorld
{
    int a;
    union {
        int c;
        int d;
    } e[4];
};
",
                compilation =>
                {
                    Assert.False(compilation.HasErrors);

                    Assert.Equal(1, compilation.Classes.Count);

                    {
                        var cppStruct = compilation.Classes[0];

                        // Only one union
                        Assert.Equal(1, cppStruct.Classes.Count);

                        // Only 2 fields
                        Assert.Equal(2, cppStruct.Fields.Count);

                        // Check the union
                        Assert.Equal("e", cppStruct.Fields[1].Name);
                        Assert.IsType<CppArrayType>(cppStruct.Fields[1].Type);
                        var cppArrayType = ((CppArrayType)cppStruct.Fields[1].Type);
                        Assert.IsType<CppClass>(cppArrayType.ElementType);
                        var cppUnion = ((CppClass)cppArrayType.ElementType);
                        Assert.Equal(CppClassKind.Union, cppUnion.ClassKind);
                        Assert.Equal(2, cppUnion.Fields.Count);
                    }
                }
            );
        }
    }
}
