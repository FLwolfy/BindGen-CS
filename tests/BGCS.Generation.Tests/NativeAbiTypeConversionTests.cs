using System;
using System.Linq;
using BGCS.Configuration;
using BGCS.Conversion;
using BGCS.Core.Targeting;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Metadata;
using BGCS.CppAst.Parsing;
using BGCS.CppAst.Targeting;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeAbiTypeConversionTests
{
    [Theory]
    [InlineData("windows", "x64", "msvc", "int", "uint", "char", "double")]
    [InlineData("linux", "x64", "gnu", "long", "ulong", "int", "NativeLongDouble16")]
    [InlineData("macos", "arm64", "darwin", "long", "ulong", "int", "double")]
    public void TypeConverter_ShouldHonorTargetPrimitiveWidths(
        string platform,
        string architecture,
        string abi,
        string expectedLong,
        string expectedUnsignedLong,
        string expectedWchar,
        string expectedLongDouble)
    {
        CppParserOptions options = new()
        {
            parserKind = CppParserKind.Cpp,
            parseMacros = false,
            parseComments = false,
            parseSystemIncludes = false
        };
        options.ConfigureForTarget(new ClangTargetResolver().Resolve(new(new NativeTargetId(string.Join("-", platform, architecture, abi)))), discoverHostToolchain: false);
        CppCompilation compilation = CppParser.Parse(
            "struct NativeAbiValues { long signedLong; unsigned long unsignedLong; wchar_t wide; long double extendedValue; };",
            options);

        Assert.False(compilation.hasErrors, string.Join(Environment.NewLine, compilation.diagnostics.messages));
        CppClass type = Assert.Single(compilation.classes, value => value.name == "NativeAbiValues");
        CsCodeGeneratorConfig config = new();

        Assert.Equal(expectedLong, config.typeConverter.Convert(type.fields.Single(field => field.name == "signedLong").type, CsTypeStyle.Raw));
        Assert.Equal(expectedUnsignedLong, config.typeConverter.Convert(type.fields.Single(field => field.name == "unsignedLong").type, CsTypeStyle.Raw));
        Assert.Equal(expectedWchar, config.typeConverter.Convert(type.fields.Single(field => field.name == "wide").type, CsTypeStyle.Raw));
        Assert.Equal(expectedLongDouble, config.typeConverter.Convert(type.fields.Single(field => field.name == "extendedValue").type, CsTypeStyle.Raw));
    }
}
