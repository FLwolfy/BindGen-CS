using System;
using System.Runtime.InteropServices;
using System.Text;
using Xunit;

namespace BGCS.Runtime.Tests;

public sealed unsafe class CountedStringCodecTests
{
    [Theory]
    [InlineData(1, false, 7)]
    [InlineData(1, true, 7)]
    [InlineData(2, false, 5)]
    [InlineData(2, true, 5)]
    [InlineData(4, false, 4)]
    [InlineData(4, true, 4)]
    public void ExactCountsPreserveEmbeddedZeroAndNonBmpUnits(int width, bool bigEndian, int expected)
    {
        using NativeStringLease lease = Encode(width, bigEndian, "A\0😀B", false, 7);
        Assert.Equal(expected, lease.encodedCodeUnitCount);
        Assert.NotEqual(0, lease.pointer);
        Assert.Equal((nuint)0, (nuint)lease.pointer % 64);
        Assert.Equal("A\0😀B", width switch
        {
            1 => Utf8Codec.DecodeLength(lease.pointer, expected),
            2 => Utf16Codec.DecodeLength(lease.pointer, expected, bigEndian),
            _ => Utf32Codec.DecodeLength(lease.pointer, expected, bigEndian)
        });
        byte* bytes = (byte*)lease.pointer;
        Assert.Equal((byte)65, bytes[width > 1 && bigEndian ? width - 1 : 0]);
        for (int index = 0; index < width; index++) Assert.Equal(0, bytes[expected * width + index]);
        if (width == 1) Assert.Equal(new byte[] { 65, 0, 0xf0, 0x9f, 0x98, 0x80, 66 }, new ReadOnlySpan<byte>(bytes, expected).ToArray());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void NullAndEmptyRetainDifferentAddressSemantics(int width)
    {
        using NativeStringLease absent = Encode(width, false, null, true, 1);
        using NativeStringLease empty = Encode(width, false, "", false, 1);
        Assert.Equal(0, absent.pointer);
        Assert.Equal(0, absent.encodedCodeUnitCount);
        Assert.NotEqual(0, empty.pointer);
        Assert.Equal(0, empty.encodedCodeUnitCount);
        Assert.Throws<ArgumentNullException>(() => Encode(width, false, null, false, 1));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void InvalidUnicodeAndBoundsFailBeforeCustomAllocation(int width)
    {
        int allocations = 0, frees = 0;
        NativeAllocator allocator = new("counted-text-test", (length, alignment) =>
        { allocations++; return (nint)NativeMemory.AlignedAlloc(length, alignment); },
            (address, _, _) => { frees++; NativeMemory.AlignedFree((void*)address); }, NativeAllocatorThreadDomain.AnyThread);
        Assert.Throws<EncoderFallbackException>(() => Encode(width, false, "\ud800", false, 8, allocator));
        Assert.Throws<ArgumentOutOfRangeException>(() => Encode(width, false, "AB", false, 1, allocator));
        Assert.Throws<ArgumentOutOfRangeException>(() => Encode(width, false, "", false, 0, allocator));
        Assert.Equal(0, allocations);
        using NativeStringLease valid = Encode(width, false, "AB", false, 2, allocator);
        Assert.Equal(1, allocations);
        valid.Dispose(); valid.Dispose();
        Assert.Equal(1, frees);
    }

    [Theory]
    [InlineData(1, 6)]
    [InlineData(2, 4)]
    [InlineData(4, 3)]
    public void ExistingTerminatedInputsExposeCountsWithoutChangingTruncationPolicy(int width, int expected)
    {
        using NativeStringLease lease = width switch
        {
            1 => Utf8Codec.EncodeNullTerminated("A😀B", false),
            2 => Utf16Codec.EncodeNullTerminated("A😀B", false),
            _ => Utf32Codec.EncodeNullTerminated("A😀B", false)
        };
        Assert.Equal(expected, lease.encodedCodeUnitCount);
        Assert.Throws<ArgumentException>(() => width switch
        {
            1 => Utf8Codec.EncodeNullTerminated("A\0B", false),
            2 => Utf16Codec.EncodeNullTerminated("A\0B", false),
            _ => Utf32Codec.EncodeNullTerminated("A\0B", false)
        });
    }

    private static NativeStringLease Encode(int width, bool bigEndian, string? value, bool allowsNull, int maximum, NativeAllocator? allocator = null)
        => width switch
        {
            1 => Utf8Codec.EncodeCounted(value, allowsNull, 64, allocator, maximum),
            2 => Utf16Codec.EncodeCounted(value, allowsNull, bigEndian, 64, allocator, maximum),
            _ => Utf32Codec.EncodeCounted(value, allowsNull, bigEndian, 64, allocator, maximum)
        };
}
