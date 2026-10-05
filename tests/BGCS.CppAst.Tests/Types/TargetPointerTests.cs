using System;
using BGCS.CppAst.Model.Types;
using BGCS.CppAst.Parsing;
using Xunit;

namespace BGCS.CppAst.Tests;

public sealed class TargetPointerTests
{
    [Theory]
    [InlineData("i686-pc-windows-msvc", 4)]
    [InlineData("aarch64-unknown-linux-gnu", 8)]
    public void CanonicalPointerRetainsTheParsedTargetWidth(
        string triple,
        int pointerSize
    ) {
        using var compilation = CppParser.Parse("typedef int Value; Value* value;", new CppParserOptions
        {
            targetTriple = triple,
            autoSquashTypedef = false
        });
        Assert.False(compilation.hasErrors, compilation.diagnostics.ToString());
        CppType pointer = Assert.Single(compilation.fields).type;
        Assert.Equal(pointerSize, compilation.pointerSize);
        Assert.Equal(pointerSize, pointer.sizeOf);
        CppPointerType canonical = Assert.IsType<CppPointerType>(pointer.GetCanonicalType());
        Assert.Equal(pointerSize, canonical.sizeOf);
        Assert.IsType<CppPrimitiveType>(canonical.elementType);
    }

    [Fact]
    public void SyntheticPointerRequiresAnExplicitPositiveTargetWidth()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CppPointerType(default, CppPrimitiveType.@int, 0));
        CppPointerType pointer = new(default, CppPrimitiveType.@int, 4);
        Assert.Equal(4, pointer.GetCanonicalType().sizeOf);
    }
}
