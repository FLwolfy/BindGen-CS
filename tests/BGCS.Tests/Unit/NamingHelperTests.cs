using System;
using System.Globalization;
using BGCS.Configuration.Naming;
using Xunit;

namespace BGCS.Tests;

public sealed class NamingHelperTests
{
    [Fact]
    public void Capitalize_DoesNotModifyTheOriginalOrInternedString()
    {
        const string input = "sHARED";
        Assert.Equal("Shared", input.Capitalize());
        Assert.Equal("sHARED", input);
        Assert.Equal("sHARED", string.Intern(input));
        Assert.Equal(string.Empty, string.Empty.Capitalize());
        Assert.Throws<ArgumentNullException>(() => NamingHelper.Capitalize(null!));
    }

    [Theory]
    [InlineData("", NamingConvention.Unknown)]
    [InlineData("123", NamingConvention.Unknown)]
    [InlineData("native", NamingConvention.CamelCase)]
    [InlineData("NATIVE", NamingConvention.UpperFlatCase)]
    [InlineData("nativeValue", NamingConvention.CamelCase)]
    [InlineData("NativeValue", NamingConvention.PascalCase)]
    [InlineData("native_value", NamingConvention.SnakeCase)]
    [InlineData("NATIVE_VALUE", NamingConvention.ScreamingSnakeCase)]
    public void AnalyzeNamingConvention_DistinguishesFlatAndMixedCase(
        string input,
        NamingConvention expected
    ) {
        Assert.Equal(expected, NamingHelper.AnalyzeNamingConvention(input));
    }

    [Fact]
    public void IdentifierConversion_IsIndependentOfTheProcessCulture()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Assert.Equal("Input", "iNPUT".Capitalize());
            Assert.Equal("input_value", NamingHelper.ConvertTo("InputValue", NamingConvention.SnakeCase));
            Assert.Equal("INPUTVALUE", NamingHelper.ConvertTo("inputValue", NamingConvention.UpperFlatCase));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
