using System;
using System.Text;
using BGCS.Runtime;
using Xunit;

namespace BGCS.Runtime.Tests;

public sealed class WideTextInputTests
{
    [Fact]
    public unsafe void NativeOrder_UsesDistinctUtf16AndUtf32UnitsAndAlignedStorage()
    {
        using NativeStringLease utf16 = Utf16Codec.EncodeNullTerminated("A🐈", false, requiredAlignment: 64);
        using NativeStringLease utf32 = Utf32Codec.EncodeNullTerminated("A🐈", false, requiredAlignment: 64);
        Assert.Equal((nuint)0, (nuint)utf16.pointer % 64);
        Assert.Equal((nuint)0, (nuint)utf32.pointer % 64);
        Assert.Equal(new ushort[] { 65, 0xd83d, 0xdc08, 0 }, new ReadOnlySpan<ushort>((void*)utf16.pointer, 4).ToArray());
        Assert.Equal(new uint[] { 65, 0x1f408, 0 }, new ReadOnlySpan<uint>((void*)utf32.pointer, 3).ToArray());
        Assert.Equal("A🐈", Utf16Codec.Decode(utf16.pointer, 4));
        Assert.Equal("A🐈", Utf32Codec.Decode(utf32.pointer, 3));
        using NativeStringLease regression = Utf32Codec.EncodeNullTerminated("AB", false);
        Assert.Equal((uint)65, *(uint*)regression.pointer);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public unsafe void ExplicitByteOrder_IsPreservedIndependentlyOfTheProcess(bool bigEndian)
    {
        using NativeStringLease utf16 = Utf16Codec.EncodeNullTerminated("AB", false, bigEndian);
        using NativeStringLease utf32 = Utf32Codec.EncodeNullTerminated("AB", false, bigEndian);
        Assert.Equal(bigEndian ? new byte[] { 0, 65, 0, 66, 0, 0 } : new byte[] { 65, 0, 66, 0, 0, 0 }, new ReadOnlySpan<byte>((void*)utf16.pointer, 6).ToArray());
        Assert.Equal(bigEndian ? new byte[] { 0, 0, 0, 65, 0, 0, 0, 66, 0, 0, 0, 0 } : new byte[] { 65, 0, 0, 0, 66, 0, 0, 0, 0, 0, 0, 0 }, new ReadOnlySpan<byte>((void*)utf32.pointer, 12).ToArray());
        Assert.Equal("AB", Utf16Codec.Decode(utf16.pointer, 3, bigEndian));
        Assert.Equal("AB", Utf32Codec.Decode(utf32.pointer, 3, bigEndian));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    public void InvalidUnicodeNullAllowanceAndAlignment_AreExplicit(int width)
    {
        NativeStringLease Encode(string? value, bool allowsNull = false, nuint alignment = 4)
            => width == 16 ? Utf16Codec.EncodeNullTerminated(value, allowsNull, requiredAlignment: alignment) : Utf32Codec.EncodeNullTerminated(value, allowsNull, requiredAlignment: alignment);
        Assert.Throws<ArgumentNullException>(() => Encode(null));
        using NativeStringLease absent = Encode(null, true);
        Assert.Equal(0, absent.pointer);
        Assert.Throws<ArgumentException>(() => Encode("A\0B"));
        Assert.Throws<EncoderFallbackException>(() => Encode("\ud800"));
        Assert.Throws<EncoderFallbackException>(() => Encode("\udc00"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Encode("A", alignment: 3));
        using NativeStringLease value = Encode("AB");
        value.Dispose();
        value.Dispose();
        Assert.Equal(0, value.pointer);
    }

    [Fact]
    public unsafe void Decode_RejectsInvalidUnitsAndMissingTerminatorsWithinTheReadableBound()
    {
        ushort* utf16 = stackalloc ushort[] { 0xd800, 0 };
        uint* utf32 = stackalloc uint[] { 0x110000, 0 };
        nint first = (nint)utf16, second = (nint)utf32;
        Assert.Throws<DecoderFallbackException>(() => Utf16Codec.Decode(first, 2));
        Assert.Throws<DecoderFallbackException>(() => Utf32Codec.Decode(second, 2));
        Assert.Throws<ArgumentException>(() => Utf16Codec.Decode(first, 1));
        Assert.Throws<ArgumentException>(() => Utf32Codec.Decode(second, 1));
    }
}
