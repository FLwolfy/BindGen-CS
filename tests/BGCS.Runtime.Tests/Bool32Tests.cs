using BGCS.Runtime;
using Xunit;

namespace BGCS.Runtime.Tests;

public class Bool32Tests
{
    [Fact]
    public void ImplicitConversions_ShouldBehaveAsExpected()
    {
        Bool32 t = true;
        Bool32 f = false;

        Assert.True((bool)t);
        Assert.False((bool)f);
        Assert.Equal(1, (int)t);
        Assert.Equal(0, (int)f);
    }

    [Fact]
    public void Equality_And_Inequality_ShouldBehaveAsExpected()
    {
        Bool32 a = new(1);
        Bool32 b = new(true);
        Bool32 c = new(0);

        Assert.True(a == b);
        Assert.False(a != b);
        Assert.True(a != c);
    }

    [Fact]
    public void ToString_ShouldMatchValue()
    {
        Assert.Equal("true", new Bool32(1).ToString());
        Assert.Equal("false", new Bool32(0).ToString());
    }

    [Fact]
    public void NonZeroValue_ShouldConvertToTrue()
    {
        Bool32 value = new(123);
        Assert.True((bool)value);
    }
}
