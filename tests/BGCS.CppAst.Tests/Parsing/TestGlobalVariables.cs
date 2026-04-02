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
    public class TestGlobalVariables : InlineTestBase
    {
        [Fact]
        public void TestSimple()
        {
            ParseAssert(@"
int var0;
int var1;
extern int var2;
const int var3 = 123;
const unsigned int var4 = (unsigned int) 125;
",
                compilation =>
                {
                    Assert.False(compilation.HasErrors);

                    Assert.Equal(5, compilation.Fields.Count);

                    {
                        var cppField = compilation.Fields[0];
                        Assert.Equal("var0", cppField.Name);
                        Assert.Equal(CppTypeKind.Primitive, cppField.Type.TypeKind);
                        Assert.Equal(CppPrimitiveKind.Int, ((CppPrimitiveType)cppField.Type).Kind);
                        Assert.Equal(CppVisibility.Default, cppField.Visibility);
                        Assert.Equal(CppStorageQualifier.None, cppField.StorageQualifier);
                    }

                    {
                        var cppField = compilation.Fields[1];
                        Assert.Equal("var1", cppField.Name);
                        Assert.Equal(CppTypeKind.Primitive, cppField.Type.TypeKind);
                        Assert.Equal(CppVisibility.Default, cppField.Visibility);
                        Assert.Equal(CppPrimitiveKind.Int, ((CppPrimitiveType)cppField.Type).Kind);
                        Assert.Equal(CppStorageQualifier.None, cppField.StorageQualifier);
                    }

                    {
                        var cppField = compilation.Fields[2];
                        Assert.Equal("var2", cppField.Name);
                        Assert.Equal(CppTypeKind.Primitive, cppField.Type.TypeKind);
                        Assert.Equal(CppVisibility.Default, cppField.Visibility);
                        Assert.Equal(CppPrimitiveKind.Int, ((CppPrimitiveType)cppField.Type).Kind);
                        Assert.Equal(CppStorageQualifier.Extern, cppField.StorageQualifier);
                    }

                    {
                        var cppField = compilation.Fields[3];
                        Assert.Equal("var3", cppField.Name);
                        Assert.Equal(CppTypeKind.Qualified, cppField.Type.TypeKind);
                        Assert.Equal(CppTypeQualifier.Const, ((CppQualifiedType)cppField.Type).Qualifier);
                        Assert.NotNull(cppField.InitExpression);
                        Assert.Equal("123", cppField.InitExpression.ToString());
                    }

                    {
                        var cppField = compilation.Fields[4];
                        Assert.Equal("var4", cppField.Name);
                        Assert.Equal(CppTypeKind.Qualified, cppField.Type.TypeKind);
                        Assert.Equal(CppTypeQualifier.Const, ((CppQualifiedType)cppField.Type).Qualifier);
                        Assert.NotNull(cppField.InitExpression);
                        Assert.Equal("(unsigned int)125", cppField.InitExpression.ToString());
                    }
                }
            );
        }
    }
}