using BGCS.Runtime;
using Xunit;

namespace BGCS.Runtime.Tests;

public class Bool8AndBitfieldTests
{
    [Fact]
    public void Bool8_ImplicitConversions_ShouldBehaveAsExpected()
    {
        Bool8 t = true;
        Bool8 f = false;

        Assert.True((bool)t);
        Assert.False((bool)f);
        Assert.Equal((byte)1, (byte)t);
        Assert.Equal((byte)0, (byte)f);
    }

    [Fact]
    public void Bool8_EqualityAndToString_ShouldBehaveAsExpected()
    {
        Bool8 a = new((byte)1);
        Bool8 b = new(true);
        Bool8 c = new((byte)0);

        Assert.True(a == b);
        Assert.True(a != c);
        Assert.Equal("true", a.ToString());
        Assert.Equal("false", c.ToString());
    }

    [Fact]
    public void Bitfield_SetAndGet_UInt32_ShouldReadBackAssignedValue()
    {
        uint raw = 0;

        Bitfield.Set(ref raw, 0b101u, offset: 4, bitWidth: 3);
        uint extracted = Bitfield.Get(raw, offset: 4, bitWidth: 3);

        Assert.Equal(0b101u, extracted);
    }

    [Fact]
    public void Bitfield_Set_ShouldPreserveUnrelatedBits()
    {
        uint raw = 0b1111_0000u;

        Bitfield.Set(ref raw, 0b01u, offset: 1, bitWidth: 2);

        Assert.Equal(0b1111_0010u, raw);
    }
}
