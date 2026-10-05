using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Types;
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
                    Assert.False(compilation.hasErrors);

                    Assert.Equal(5, compilation.fields.Count);

                    {
                        var cppField = compilation.fields[0];
                        Assert.Equal("var0", cppField.name);
                        Assert.Equal(CppTypeKind.Primitive, cppField.type.typeKind);
                        Assert.Equal(CppPrimitiveKind.Int, ((CppPrimitiveType)cppField.type).kind);
                        Assert.Equal(CppVisibility.Default, cppField.visibility);
                        Assert.Equal(CppStorageQualifier.None, cppField.storageQualifier);
                    }

                    {
                        var cppField = compilation.fields[1];
                        Assert.Equal("var1", cppField.name);
                        Assert.Equal(CppTypeKind.Primitive, cppField.type.typeKind);
                        Assert.Equal(CppVisibility.Default, cppField.visibility);
                        Assert.Equal(CppPrimitiveKind.Int, ((CppPrimitiveType)cppField.type).kind);
                        Assert.Equal(CppStorageQualifier.None, cppField.storageQualifier);
                    }

                    {
                        var cppField = compilation.fields[2];
                        Assert.Equal("var2", cppField.name);
                        Assert.Equal(CppTypeKind.Primitive, cppField.type.typeKind);
                        Assert.Equal(CppVisibility.Default, cppField.visibility);
                        Assert.Equal(CppPrimitiveKind.Int, ((CppPrimitiveType)cppField.type).kind);
                        Assert.Equal(CppStorageQualifier.Extern, cppField.storageQualifier);
                    }

                    {
                        var cppField = compilation.fields[3];
                        Assert.Equal("var3", cppField.name);
                        Assert.Equal(CppTypeKind.Qualified, cppField.type.typeKind);
                        Assert.Equal(CppTypeQualifier.Const, ((CppQualifiedType)cppField.type).qualifier);
                        Assert.NotNull(cppField.initExpression);
                        Assert.Equal("123", cppField.initExpression.ToString());
                    }

                    {
                        var cppField = compilation.fields[4];
                        Assert.Equal("var4", cppField.name);
                        Assert.Equal(CppTypeKind.Qualified, cppField.type.typeKind);
                        Assert.Equal(CppTypeQualifier.Const, ((CppQualifiedType)cppField.type).qualifier);
                        Assert.NotNull(cppField.initExpression);
                        Assert.Equal("(unsigned int)125", cppField.initExpression.ToString());
                    }
                }
            );
        }
    }
}