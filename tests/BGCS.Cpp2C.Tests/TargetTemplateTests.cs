using System.Linq;
using BGCS.Cpp2C.Configuration;
using BGCS.CppAst.Parsing;
using Xunit;

namespace BGCS.Cpp2C.Tests;

public sealed class TargetTemplateTests
{
    [Theory]
    [InlineData("i686-pc-windows-msvc", 4, 4)]
    [InlineData("aarch64-unknown-linux-gnu", 8, 8)]
    public void TemplatePacksRetainParsedTypesAndTargetLayouts(
        string triple,
        int longSize,
        int pointerSize
    ) {
        using var compilation = CppParser.Parse(
            "template<class... Types> class Values {}; Values<long, double, int*> create();",
            new CppParserOptions { targetTriple = triple });
        Assert.False(compilation.hasErrors, compilation.diagnostics.ToString());
        var arguments = new Cpp2CGeneratorConfig().GetTemplateTypeArguments(Assert.Single(compilation.functions).returnType);
        Assert.Equal(3, arguments.Count);
        Assert.Equal(new[] { longSize, 8, pointerSize }, arguments.Select(type => type.sizeOf));
    }
}
