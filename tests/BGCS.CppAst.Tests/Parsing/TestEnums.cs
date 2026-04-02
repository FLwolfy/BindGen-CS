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
    public class TestEnums : InlineTestBase
    {
        [Fact]
        public void TestSimple()
        {
            ParseAssert(@"
enum Enum0
{
    Enum0_item0,
    Enum0_item1,
    Enum0_item2
};

enum class Enum1
{
    item0,
    item1,
    item2
};

enum class Enum2 : short
{
    item0 = 3,
    item1 = 4,
    item2 = 5
};
",
                compilation =>
                {
                    Assert.False(compilation.HasErrors);

                    Assert.Equal(3, compilation.Enums.Count);

                    {
                        var cppEnum = compilation.Enums[0];
                        Assert.Equal("Enum0", cppEnum.Name);
                        Assert.Equal(CppTypeKind.Primitive, cppEnum.IntegerType.TypeKind);
                        Assert.Equal(CppPrimitiveKind.Int, ((CppPrimitiveType)cppEnum.IntegerType).Kind);
                        Assert.Equal(3, cppEnum.Items.Count);
                        Assert.Equal(sizeof(int), cppEnum.SizeOf);
                        Assert.False(cppEnum.IsScoped);
                        Assert.Equal("Enum0_item0", cppEnum.Items[0].Name);
                        Assert.Equal("Enum0_item1", cppEnum.Items[1].Name);
                        Assert.Equal("Enum0_item2", cppEnum.Items[2].Name);
                        Assert.Equal(0, cppEnum.Items[0].Value);
                        Assert.Equal(1, cppEnum.Items[1].Value);
                        Assert.Equal(2, cppEnum.Items[2].Value);

                        var cppEnum1 = compilation.FindByName<CppEnum>("Enum0");
                        Assert.Equal(cppEnum, cppEnum1);
                    }

                    {
                        var cppEnum = compilation.Enums[1];
                        Assert.Equal("Enum1", cppEnum.Name);
                        Assert.Equal(CppTypeKind.Primitive, cppEnum.IntegerType.TypeKind);
                        Assert.Equal(CppPrimitiveKind.Int, ((CppPrimitiveType)cppEnum.IntegerType).Kind);
                        Assert.Equal(3, cppEnum.Items.Count);
                        Assert.Equal(sizeof(int), cppEnum.SizeOf);
                        Assert.True(cppEnum.IsScoped);
                        Assert.Equal("item0", cppEnum.Items[0].Name);
                        Assert.Equal("item1", cppEnum.Items[1].Name);
                        Assert.Equal("item2", cppEnum.Items[2].Name);
                        Assert.Equal(0, cppEnum.Items[0].Value);
                        Assert.Equal(1, cppEnum.Items[1].Value);
                        Assert.Equal(2, cppEnum.Items[2].Value);
                    }

                    {
                        var cppEnum = compilation.Enums[2];
                        Assert.Equal("Enum2", cppEnum.Name);
                        Assert.Equal(CppTypeKind.Primitive, cppEnum.IntegerType.TypeKind);
                        Assert.Equal(CppPrimitiveKind.Short, ((CppPrimitiveType)cppEnum.IntegerType).Kind);
                        Assert.Equal(3, cppEnum.Items.Count);
                        Assert.Equal(sizeof(short), cppEnum.SizeOf);
                        Assert.True(cppEnum.IsScoped);
                        Assert.Equal("item0", cppEnum.Items[0].Name);
                        Assert.Equal("item1", cppEnum.Items[1].Name);
                        Assert.Equal("item2", cppEnum.Items[2].Name);
                        Assert.Equal(3, cppEnum.Items[0].Value);
                        Assert.Equal(4, cppEnum.Items[1].Value);
                        Assert.Equal(5, cppEnum.Items[2].Value);
                    }
                }
            );
        }
    }
}