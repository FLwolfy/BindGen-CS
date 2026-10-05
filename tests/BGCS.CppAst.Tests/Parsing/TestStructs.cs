using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Types;
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
                    Assert.False(compilation.hasErrors);

                    Assert.Equal(4, compilation.classes.Count);

                    {
                        var cppStruct = compilation.classes[0];
                        Assert.Equal("Struct0", cppStruct.name);
                        Assert.Empty(cppStruct.fields);
                        Assert.Equal(sizeof(byte), cppStruct.sizeOf);
                        Assert.Equal(1, cppStruct.alignOf);
                    }

                    {
                        var cppStruct = compilation.classes[1];
                        Assert.Equal("Struct1", cppStruct.name);
                        Assert.Empty(cppStruct.fields);
                        Assert.Single(cppStruct.baseTypes);
                        Assert.True(cppStruct.baseTypes[0].type is CppClass);
                        Assert.True(ReferenceEquals(compilation.classes[0], cppStruct.baseTypes[0].type));
                        Assert.Equal(sizeof(byte), cppStruct.sizeOf);
                        Assert.Equal(1, cppStruct.alignOf);
                    }

                    {
                        var cppStruct = compilation.classes[2];
                        Assert.Equal("Struct2", cppStruct.name);
                        Assert.Single(cppStruct.fields);
                        Assert.Equal("field0", cppStruct.fields[0].name);
                        Assert.Equal(CppTypeKind.Primitive, cppStruct.fields[0].type.typeKind);
                        Assert.Equal(CppPrimitiveKind.Int, ((CppPrimitiveType)cppStruct.fields[0].type).kind);
                        Assert.Equal(sizeof(int), cppStruct.sizeOf);
                        Assert.Equal(4, cppStruct.alignOf);
                    }

                    {
                        var cppStruct = compilation.classes[3];
                        Assert.Equal(2, cppStruct.fields.Count);
                        Assert.Equal("field0", cppStruct.fields[0].name);
                        Assert.Equal(CppTypeKind.Primitive, cppStruct.fields[0].type.typeKind);
                        Assert.Equal(CppPrimitiveKind.Int, ((CppPrimitiveType)cppStruct.fields[0].type).kind);
                        Assert.Equal(CppVisibility.Private, cppStruct.fields[0].visibility);

                        Assert.Equal("field1", cppStruct.fields[1].name);
                        Assert.Equal(CppTypeKind.Primitive, cppStruct.fields[1].type.typeKind);
                        Assert.Equal(CppPrimitiveKind.Float, ((CppPrimitiveType)cppStruct.fields[1].type).kind);
                        Assert.Equal(CppVisibility.Public, cppStruct.fields[1].visibility);
                        Assert.Equal(sizeof(int), cppStruct.fields[1].offset);
                        Assert.Equal(sizeof(int) + sizeof(float), cppStruct.sizeOf);
                        Assert.Equal(4, cppStruct.alignOf);
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
                    Assert.False(compilation.hasErrors);

                    Assert.Single(compilation.classes);

                    {
                        var cppStruct = compilation.classes[0];
                        Assert.Equal(string.Empty, cppStruct.name);
                        Assert.Equal(2, cppStruct.fields.Count);
                        Assert.Equal(sizeof(int), cppStruct.fields[1].offset);
                        Assert.Equal(sizeof(int) + sizeof(int), cppStruct.sizeOf);
                        Assert.Equal(4, cppStruct.alignOf);
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
                    Assert.False(compilation.hasErrors);

                    Assert.Single(compilation.classes);

                    {
                        var cppStruct = compilation.classes[0];
                        Assert.Equal(4, cppStruct.fields.Count);

                        for (int i = 0; i < 4; i++)
                        {
                            Assert.Equal(i * 4, cppStruct.fields[i].offset);
                            Assert.Equal(4, cppStruct.fields[i].type.sizeOf);
                        }

                        // Check first union
                        Assert.Equal(string.Empty, cppStruct.fields[1].name);
                        Assert.IsType<CppClass>(cppStruct.fields[1].type);
                        var cppUnion = ((CppClass)cppStruct.fields[1].type);
                        Assert.Equal(CppClassKind.Union, ((CppClass)cppStruct.fields[1].type).classKind);
                        Assert.Equal(2, cppUnion.fields.Count);

                        // Check 2nd union
                        Assert.Equal(string.Empty, cppStruct.fields[3].name);
                        Assert.IsType<CppClass>(cppStruct.fields[3].type);
                        cppUnion = ((CppClass)cppStruct.fields[3].type);
                        Assert.Equal(CppClassKind.Union, ((CppClass)cppStruct.fields[3].type).classKind);
                        Assert.Equal(2, cppUnion.fields.Count);
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
                    Assert.False(compilation.hasErrors);

                    Assert.Single(compilation.classes);

                    {
                        var cppStruct = compilation.classes[0];

                        // Only one union
                        Assert.Single(cppStruct.classes);

                        // Only 2 fields
                        Assert.Equal(2, cppStruct.fields.Count);

                        // Check the union
                        Assert.Equal("e", cppStruct.fields[1].name);
                        Assert.IsType<CppClass>(cppStruct.fields[1].type);
                        var cppUnion = ((CppClass)cppStruct.fields[1].type);
                        Assert.Equal(CppClassKind.Union, ((CppClass)cppStruct.fields[1].type).classKind);
                        Assert.Equal(2, cppUnion.fields.Count);
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
                    Assert.False(compilation.hasErrors);

                    Assert.Single(compilation.classes);

                    {
                        var cppStruct = compilation.classes[0];

                        // Only one union
                        Assert.Single(cppStruct.classes);

                        // Only 2 fields
                        Assert.Equal(2, cppStruct.fields.Count);

                        // Check the union
                        Assert.Equal("e", cppStruct.fields[1].name);
                        Assert.IsType<CppArrayType>(cppStruct.fields[1].type);
                        var cppArrayType = ((CppArrayType)cppStruct.fields[1].type);
                        Assert.IsType<CppClass>(cppArrayType.elementType);
                        var cppUnion = ((CppClass)cppArrayType.elementType);
                        Assert.Equal(CppClassKind.Union, cppUnion.classKind);
                        Assert.Equal(2, cppUnion.fields.Count);
                    }
                }
            );
        }
    }
}
