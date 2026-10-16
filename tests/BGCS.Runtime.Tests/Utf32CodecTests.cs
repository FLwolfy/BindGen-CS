using System;
using System.Runtime.InteropServices;
using System.Text;
using Xunit;

namespace BGCS.Runtime.Tests;

public unsafe class Utf32CodecTests
{
    [Theory]
    [InlineData("")]
    [InlineData("AB")]
    [InlineData("Hello 世界")]
    [InlineData("A😀B")]
    public void AllocatedText_RoundTripsUnicodeScalars(string value)
    {
        nint address = Utf32Codec.Allocate(value);
        try
        {
            Assert.Equal(value, Utf32Codec.Decode(address));
            if (value.StartsWith('A'))
                Assert.Equal(65u, *(uint*)address);
        }
        finally
        {
            Marshal.FreeCoTaskMem(address);
        }
    }

    [Fact]
    public void NullString_PreservesNullPointer()
    {
        Assert.Equal(0, Utf32Codec.Allocate(null));
        Assert.Null(Utf32Codec.Decode(0));
    }

    [Fact]
    public void UnpairedSurrogate_IsRejectedBeforeAllocation()
    {
        Assert.Throws<EncoderFallbackException>(() => Utf32Codec.Allocate("\ud800"));
    }

    [Fact]
    public void InvalidNativeScalar_IsRejected()
    {
        uint* units = stackalloc uint[] { 0x110000, 0 };
        nint address = (nint)units;
        Assert.Throws<DecoderFallbackException>(() => Utf32Codec.Decode(address, 2));
    }

    [Fact]
    public void MissingTerminator_DoesNotScanOutsideDeclaredRange()
    {
        uint* units = stackalloc uint[] { 65, 66 };
        nint address = (nint)units;
        Assert.Throws<ArgumentException>(() => Utf32Codec.Decode(address, 2));
    }
}
