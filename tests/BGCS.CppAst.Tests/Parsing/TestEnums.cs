using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Types;
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
                    Assert.False(compilation.hasErrors);

                    Assert.Equal(3, compilation.enums.Count);

                    {
                        var cppEnum = compilation.enums[0];
                        Assert.Equal("Enum0", cppEnum.name);
                        Assert.Equal(CppTypeKind.Primitive, cppEnum.integerType.typeKind);
                        Assert.Equal(CppPrimitiveKind.Int, ((CppPrimitiveType)cppEnum.integerType).kind);
                        Assert.Equal(3, cppEnum.items.Count);
                        Assert.Equal(sizeof(int), cppEnum.sizeOf);
                        Assert.False(cppEnum.isScoped);
                        Assert.Equal("Enum0_item0", cppEnum.items[0].name);
                        Assert.Equal("Enum0_item1", cppEnum.items[1].name);
                        Assert.Equal("Enum0_item2", cppEnum.items[2].name);
                        Assert.Equal(0, cppEnum.items[0].value);
                        Assert.Equal(1, cppEnum.items[1].value);
                        Assert.Equal(2, cppEnum.items[2].value);

                        var cppEnum1 = compilation.FindByName<CppEnum>("Enum0");
                        Assert.Equal(cppEnum, cppEnum1);
                    }

                    {
                        var cppEnum = compilation.enums[1];
                        Assert.Equal("Enum1", cppEnum.name);
                        Assert.Equal(CppTypeKind.Primitive, cppEnum.integerType.typeKind);
                        Assert.Equal(CppPrimitiveKind.Int, ((CppPrimitiveType)cppEnum.integerType).kind);
                        Assert.Equal(3, cppEnum.items.Count);
                        Assert.Equal(sizeof(int), cppEnum.sizeOf);
                        Assert.True(cppEnum.isScoped);
                        Assert.Equal("item0", cppEnum.items[0].name);
                        Assert.Equal("item1", cppEnum.items[1].name);
                        Assert.Equal("item2", cppEnum.items[2].name);
                        Assert.Equal(0, cppEnum.items[0].value);
                        Assert.Equal(1, cppEnum.items[1].value);
                        Assert.Equal(2, cppEnum.items[2].value);
                    }

                    {
                        var cppEnum = compilation.enums[2];
                        Assert.Equal("Enum2", cppEnum.name);
                        Assert.Equal(CppTypeKind.Primitive, cppEnum.integerType.typeKind);
                        Assert.Equal(CppPrimitiveKind.Short, ((CppPrimitiveType)cppEnum.integerType).kind);
                        Assert.Equal(3, cppEnum.items.Count);
                        Assert.Equal(sizeof(short), cppEnum.sizeOf);
                        Assert.True(cppEnum.isScoped);
                        Assert.Equal("item0", cppEnum.items[0].name);
                        Assert.Equal("item1", cppEnum.items[1].name);
                        Assert.Equal("item2", cppEnum.items[2].name);
                        Assert.Equal(3, cppEnum.items[0].value);
                        Assert.Equal(4, cppEnum.items[1].value);
                        Assert.Equal(5, cppEnum.items[2].value);
                    }
                }
            );
        }
    }
}