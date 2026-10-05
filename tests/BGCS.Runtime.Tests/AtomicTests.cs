using System;
using System.Threading.Tasks;
using Xunit;

namespace BGCS.Runtime.Tests;

public sealed class AtomicTests
{
    [Fact]
    public void SignedIntegers_PreserveNegativeValuesAndCanonicalOverflow()
    {
        Atomic<int> atomic = new(-4);
        Assert.Equal(-7, atomic.Add(-3));
        atomic.value = int.MaxValue;
        Assert.Equal(int.MinValue, atomic.Increment());
        Assert.True(atomic.CompareAndSwap(int.MinValue, -1));
        Assert.Equal(-1, atomic.value);
        Assert.False(atomic.CompareAndSwap(int.MaxValue, 10));
    }

    [Fact]
    public void SmallUnsignedIntegers_WrapWithoutBreakingComparison()
    {
        Atomic<byte> atomic = new(byte.MaxValue);
        Assert.Equal((byte)0, atomic.Increment());
        Assert.True(atomic.CompareAndSwap(0, 1));
        Assert.Equal(byte.MaxValue, atomic.Add(254));
        Assert.Equal((byte)0, atomic.Add(1));
        Assert.Equal(byte.MaxValue, atomic.Decrement());
    }

    [Fact]
    public void UnsignedIntegers_KeepAllSixtyFourBits()
    {
        Atomic<ulong> atomic = new(ulong.MaxValue);
        Assert.Equal(0UL, atomic.Increment());
        Assert.Equal(ulong.MaxValue, atomic.Decrement());
        Assert.True(atomic.CompareAndSwap(ulong.MaxValue, 1UL << 63));
        Assert.Equal(1UL << 63, atomic.value);
    }

    [Fact]
    public void NativeSizedIntegers_SupportSignedAndUnsignedStorage()
    {
        Atomic<nint> signed = new(-20);
        Atomic<nuint> unsigned = new(nuint.MaxValue);
        Assert.Equal((nint)(-10), signed.Add(10));
        Assert.Equal((nuint)0, unsigned.Increment());
        Assert.True(unsigned.CompareAndSwap(0, 25));
        Assert.Equal((nuint)25, unsigned.value);
    }

    [Fact]
    public void ConcurrentUpdates_DoNotLoseIncrements()
    {
        Atomic<int> atomic = new(0);
        Parallel.For(0, 8, _ =>
        {
            for (int index = 0; index < 10_000; index++)
                atomic.Increment();
        });
        Assert.Equal(80_000, atomic.value);
    }

    [Fact]
    public void CopyingTheWrapper_CreatesIndependentStorage()
    {
        Atomic<int> original = new(10);
        Atomic<int> copy = original;
        copy.Increment();
        Assert.Equal(10, original.value);
        Assert.Equal(11, copy.value);
    }

    [Fact]
    public void OversizedIntegers_AreRejectedEvenOnDefaultWrappers()
    {
        Assert.Throws<NotSupportedException>(() => new Atomic<UInt128>(1));
        Atomic<UInt128> atomic = default;
        Assert.Throws<NotSupportedException>(() => atomic.value);
        Assert.Throws<NotSupportedException>(() => atomic.value = 1);
        Assert.Throws<NotSupportedException>(() => atomic.Increment());
        Assert.Throws<NotSupportedException>(() => atomic.Decrement());
        Assert.Throws<NotSupportedException>(() => atomic.Add(1));
        Assert.Throws<NotSupportedException>(() => atomic.CompareAndSwap(0, 1));
    }
}
