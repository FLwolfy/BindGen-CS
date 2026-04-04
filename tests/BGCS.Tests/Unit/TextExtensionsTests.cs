using System;
using Xunit;

namespace BGCS.Tests;

public class TextExtensionsTests
{
    [Fact]
    public void TryTrimStartFirstOccurrence_Char_ShouldTrimOnce()
    {
        ReadOnlySpan<char> input = "   #define".AsSpan();
        bool ok = input.TryTrimStartFirstOccurrence('#', out ReadOnlySpan<char> result);

        Assert.True(ok);
        Assert.Equal("define", result.ToString());
    }

    [Fact]
    public void TryTrimEndFirstOccurrence_Char_ShouldTrimOnce()
    {
        ReadOnlySpan<char> input = "value;   ".AsSpan();
        bool ok = input.TryTrimEndFirstOccurrence(';', out ReadOnlySpan<char> result);

        Assert.True(ok);
        Assert.Equal("value", result.ToString());
    }

    [Fact]
    public void TryTrimStartFirstOccurrence_String_ShouldTrimOnce()
    {
        ReadOnlySpan<char> input = "   prefixName".AsSpan();
        bool ok = input.TryTrimStartFirstOccurrence("prefix".AsSpan(), out ReadOnlySpan<char> result);

        Assert.True(ok);
        Assert.Equal("Name", result.ToString());
    }

    [Fact]
    public void TrimEndFirstOccurrence_String_NoMatchShouldReturnOriginalTrimmed()
    {
        ReadOnlySpan<char> input = "value   ".AsSpan();
        ReadOnlySpan<char> result = input.TrimEndFirstOccurrence("xyz".AsSpan());

        Assert.Equal("value", result.ToString());
    }
}
