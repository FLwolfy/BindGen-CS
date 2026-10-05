using System;
using System.Globalization;
using BGCS.Configuration;
using BGCS.Configuration.Naming;
using BGCS.Conversion;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Metadata;
using BGCS.Emission;
using BGCS.Text;
using Xunit;

namespace BGCS.Tests;

public sealed class NamingBoundaryTests
{
    [Fact]
    public void EmptyFragmentsRemainEmpty()
    {
        Assert.Equal(string.Empty, string.Empty.ToTitleCaseFragment());
        Assert.Empty(string.Empty.SplitByCase());
        Assert.Throws<ArgumentNullException>(() => IdentifierTextExtensions.ToTitleCaseFragment(null!));
    }

    [Fact]
    public void IdentifierCasingDoesNotDependOnTheCurrentCulture()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Assert.Equal("Identifier2Input", "IDENTIFIER2INPUT".ToTitleCaseFragment());
            Assert.Equal(new[] { "XML", "Identifier", "2", "Input" }, "XMLIdentifier2Input".SplitByCase());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void ParameterCacheTracksTheCurrentNamingAndKeywordPolicy()
    {
        CsCodeGeneratorConfig configuration = new() { parameterNamingConvention = NamingConvention.CamelCase };
        Assert.Equal("someValue", configuration.NormalizeParameterName("SomeValue"));
        configuration.parameterNamingConvention = NamingConvention.SnakeCase;
        Assert.Equal("some_value", configuration.NormalizeParameterName("SomeValue"));
        configuration.keywords.Add("some_value");
        Assert.Equal("@some_value", configuration.NormalizeParameterName("SomeValue"));
        configuration.keywords.Remove("some_value");
        Assert.Equal("some_value", configuration.NormalizeParameterName("SomeValue"));
    }

    [Fact]
    public void SourceLocationUsesEscapedStringLiterals()
    {
        CppMacro macro = new(default, "TEST")
        {
            span = new(new CppSourceLocation("C:\\headers\\quoted\"file.h", 0, 1, 1), default)
        };
        string attribute = macro.FormatLocationAttribute();
        Assert.Contains("C:\\\\headers\\\\quoted\\\"file.h", attribute);
    }

    [Fact]
    public void CachedNameStemsApplyCurrentReplacementMappings()
    {
        CsCodeGeneratorConfig configuration = new();
        Assert.Equal("SomeValue", configuration.GetCsCleanName("SomeValue"));
        Assert.Equal("some_value", configuration.GetCsCleanNameWithConvention("SomeValue", NamingConvention.SnakeCase));
        configuration.nameMappings["Value"] = "Item";
        Assert.Equal("SomeItem", configuration.GetCsCleanName("SomeValue"));
        Assert.Equal("some_Item", configuration.GetCsCleanNameWithConvention("SomeValue", NamingConvention.SnakeCase));
        configuration.nameMappings.Clear();
        Assert.Equal("SomeValue", configuration.GetCsCleanName("SomeValue"));
    }

    [Theory]
    [InlineData("S16")]
    [InlineData("S24")]
    [InlineData("S32")]
    [InlineData("F32")]
    [InlineData("XYZZY2048Q")]
    [InlineData("identifier9opaque")]
    public void FlatIdentifierSegmentationPreservesEveryCharacter(string identifier)
    {
        foreach (NamingConvention convention in new[]
            { NamingConvention.Unknown, NamingConvention.LowerFlatCase, NamingConvention.UpperFlatCase })
        {
            Assert.Equal(identifier, string.Concat(NamingHelper.GetParts(identifier, convention)));
        }
    }

    [Theory]
    [InlineData("ma_format_u8", "U8")]
    [InlineData("ma_format_s16", "S16")]
    [InlineData("ma_format_s24", "S24")]
    [InlineData("ma_format_s32", "S32")]
    [InlineData("ma_format_f32", "F32")]
    public void EnumProjectionPreservesNumericFormatIdentity(
        string nativeName,
        string managedName
    ) {
        var config = new CsCodeGeneratorConfig();
        Assert.Equal(managedName, config.GetEnumName(nativeName, config.GetEnumNamePrefix("ma_format")));
    }

    [Theory]
    [InlineData("quoted\"value", false, "quoted\\\"value")]
    [InlineData("quoted\"value", true, "\"quoted\\\"value\"")]
    [InlineData("line\n\\path", false, "line\\n\\\\path")]
    public void LiteralEscapingProducesExactlyOneCSharpEncoding(
        string value,
        bool quote,
        string expected
    ) {
        Assert.Equal(expected, value.ToLiteral(quote));
    }
}
