using System;
using BGCS.Runtime;
using Xunit;

namespace BGCS.Runtime.Tests;

public sealed class BitfieldByteRangeTests
{
    [Theory]
    [InlineData(0, 3, 7UL)]
    [InlineData(3, 20, 0xABCDEUL)]
    [InlineData(20, 10, 0x3FFUL)]
    [InlineData(7, 64, ulong.MaxValue)]
    public void WritePreservesBitsOutsideTheField(
        int offset,
        int width,
        ulong value
    ) {
        byte[] bytes = new byte[9];
        Array.Fill(bytes, (byte)0xA5);
        byte[] previous = (byte[])bytes.Clone();
        Bitfield.Set(bytes.AsSpan(), value, offset, width);
        Assert.Equal(value, Bitfield.Get((ReadOnlySpan<byte>)bytes, offset, width));
        for (int position = 0; position < bytes.Length * 8; position++)
        {
            if (position < offset || position >= offset + width)
                Assert.Equal((previous[position / 8] >> (position % 8)) & 1, (bytes[position / 8] >> (position % 8)) & 1);
        }
    }

    [Fact]
    public void SignedReadExtendsTheDeclaredWidth()
    {
        byte[] bytes = [0, 0, 0, 0];
        Bitfield.Set(bytes.AsSpan(), unchecked((ulong)-3L), 3, 20);
        Assert.Equal(-3L, Bitfield.GetSigned(bytes.AsSpan(), 3, 20));
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 0)]
    [InlineData(0, 65)]
    [InlineData(7, 2)]
    public void InvalidWriteLeavesBytesUnchanged(
        int offset,
        int width
    ) {
        byte[] bytes = [0xA5];
        Assert.Throws<ArgumentOutOfRangeException>(() => Bitfield.Set(bytes.AsSpan(), 1, offset, width));
        Assert.Equal((byte)0xA5, bytes[0]);
    }
}
