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
}
