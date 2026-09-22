using System;
using System.Linq;
using BGCS.Conversion;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Metadata;
using BGCS.CppAst.Parsing;
using BGCS.CppAst.Targeting;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeAbiTypeConversionTests
{
    [Theory]
    [InlineData(CppTargetPlatform.Windows, CppTargetArchitecture.X64, CppTargetAbi.Msvc, "int", "uint", "char", "double")]
    [InlineData(CppTargetPlatform.Linux, CppTargetArchitecture.X64, CppTargetAbi.Gnu, "long", "ulong", "int", "NativeLongDouble16")]
    [InlineData(CppTargetPlatform.MacOS, CppTargetArchitecture.Arm64, CppTargetAbi.Darwin, "long", "ulong", "int", "double")]
    public void TypeConverter_ShouldHonorTargetPrimitiveWidths(
        CppTargetPlatform platform,
        CppTargetArchitecture architecture,
        CppTargetAbi abi,
        string expectedLong,
        string expectedUnsignedLong,
        string expectedWchar,
        string expectedLongDouble)
    {
        CppParserOptions options = new()
        {
            ParserKind = CppParserKind.Cpp,
            ParseMacros = false,
            ParseComments = false,
            ParseSystemIncludes = false
        };
        options.ConfigureForTarget(CppTarget.Resolve(platform, architecture, abi), discoverHostToolchain: false);
        CppCompilation compilation = CppParser.Parse(
            "struct NativeAbiValues { long signedLong; unsigned long unsignedLong; wchar_t wide; long double extendedValue; };",
            options);

        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics.Messages));
        CppClass type = Assert.Single(compilation.Classes, value => value.Name == "NativeAbiValues");
        CsCodeGeneratorConfig config = new();

        Assert.Equal(expectedLong, config.TypeConverter.Convert(type.Fields.Single(field => field.Name == "signedLong").Type, CsTypeStyle.Raw));
        Assert.Equal(expectedUnsignedLong, config.TypeConverter.Convert(type.Fields.Single(field => field.Name == "unsignedLong").Type, CsTypeStyle.Raw));
        Assert.Equal(expectedWchar, config.TypeConverter.Convert(type.Fields.Single(field => field.Name == "wide").Type, CsTypeStyle.Raw));
        Assert.Equal(expectedLongDouble, config.TypeConverter.Convert(type.Fields.Single(field => field.Name == "extendedValue").Type, CsTypeStyle.Raw));
    }
}
