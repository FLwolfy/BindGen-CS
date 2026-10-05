using BGCS.Analysis.Constants;
using BGCS.Core.Text;
using Xunit;

namespace BGCS.Tests;

public class NumberHelperTests
{
    [Theory]
    [InlineData("0", NumberType.Int)]
    [InlineData("42", NumberType.Int)]
    [InlineData("4294967295", NumberType.UInt)]
    [InlineData("9223372036854775807", NumberType.Long)]
    [InlineData("18446744073709551615", NumberType.ULong)]
    [InlineData("1.5", NumberType.Double)]
    [InlineData("1.5f", NumberType.Float)]
    [InlineData("1.5m", NumberType.Decimal)]
    [InlineData("0xFF", NumberType.Int)]
    [InlineData("0xFFFFFFFF", NumberType.UInt)]
    [InlineData("-1", NumberType.Int)]
    [InlineData("-2147483648", NumberType.Int)]
    [InlineData("-9223372036854775808", NumberType.Long)]
    [InlineData("0XFFFFFFFF", NumberType.UInt)]
    [InlineData("1e2", NumberType.Double)]
    [InlineData("1E-2F", NumberType.Float)]
    [InlineData("42UL", NumberType.ULong)]
    public void IsNumeric_WithTypeInference_ShouldReturnExpectedType(string input, NumberType expectedType)
    {
        bool ok = input.IsNumeric(out NumberType type);

        Assert.True(ok);
        Assert.Equal(expectedType, type);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("12x")]
    [InlineData("1e+")]
    public void IsNumeric_InvalidValues_ShouldReturnFalse(string input)
    {
        Assert.False(input.IsNumeric());
    }

    [Fact]
    public void IsNumeric_RespectOptions_ShouldRejectMinusWhenDisabled()
    {
        bool ok = "-1".IsNumeric(NumberParseOptions.AllowHex | NumberParseOptions.AllowSuffix);
        Assert.False(ok);
    }

    [Fact]
    public void IsNumeric_AllowBrackets_ShouldAcceptWrappedNumber()
    {
        bool ok = "(123)".IsNumeric(out NumberType type, NumberParseOptions.All);
        Assert.True(ok);
        Assert.Equal(NumberType.Int, type);
    }

    [Theory]
    [InlineData("()")]
    [InlineData("-")]
    [InlineData("0x")]
    [InlineData("1..2")]
    [InlineData("0xFG")]
    [InlineData("١")]
    public void EmptyAndMalformedSyntaxDoesNotThrow(string source)
    {
        Assert.False(source.IsNumeric(NumberParseOptions.All));
        Assert.False(source.IsNumeric(out NumberType actual));
        Assert.Equal(NumberType.None, actual);
    }

    [Fact]
    public void OversizedIntegerHasValidSyntaxButNoManagedCarrier()
    {
        const string value = "18446744073709551616";
        Assert.True(value.IsNumeric(NumberParseOptions.All));
        Assert.Throws<System.IO.InvalidDataException>(() => value.IsNumeric(out _));
    }
}
