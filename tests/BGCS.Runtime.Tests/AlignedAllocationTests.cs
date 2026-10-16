using System;
using Xunit;

namespace BGCS.Runtime.Tests;

public sealed class AlignedAllocationTests
{
    [Theory]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(256)]
    public void NativeStorage_HonorsRequestedAlignment(int alignment)
    {
        using NativeAlignedBuffer buffer = new(96, (nuint)alignment);
        Assert.Equal(0u, (nuint)buffer.address % (nuint)alignment);
        Assert.Equal((nuint)96, buffer.byteLength);
        buffer.Dispose();
        buffer.Dispose();
        Assert.Throws<ObjectDisposedException>(() => buffer.address);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(12)]
    public void InvalidAlignment_IsRejected(int alignment)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NativeAlignedBuffer(96, (nuint)alignment));
    }
}
