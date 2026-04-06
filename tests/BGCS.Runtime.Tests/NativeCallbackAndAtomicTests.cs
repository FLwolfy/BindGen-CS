using System;
using BGCS.Runtime;
using Xunit;

namespace BGCS.Runtime.Tests;

public class NativeCallbackAndAtomicTests
{
    [Fact]
    public void NativeCallback_WithDelegate_ShouldAllocateHandle_AndDisposeShouldReleaseIt()
    {
        static void CallbackImpl(int value)
        {
        }

        NativeCallback<Action<int>> callback = new(CallbackImpl);

        Assert.False(callback.IsNull);
        Assert.True(callback.IsAllocated);

        callback.Dispose();

        Assert.True(callback.IsNull);
        Assert.False(callback.IsAllocated);
    }

    [Fact]
    public void NativeCallback_Equality_ShouldBeReferenceBased()
    {
        Action<int> shared = _ => { };
        NativeCallback<Action<int>> a = new(shared);
        NativeCallback<Action<int>> b = new(shared);
        NativeCallback<Action<int>> c = new(_ => { });

        try
        {
            Assert.Equal(a, b);
            Assert.True(a == b);
            Assert.True(a != c);
        }
        finally
        {
            a.Dispose();
            b.Dispose();
            c.Dispose();
        }
    }

    [Fact]
    public void Atomic_WithUlong_ShouldSupportReadWriteAndArithmetic()
    {
        Atomic<ulong> atomic = new(10);

        Assert.Equal((ulong)10, atomic.Value);

        atomic.Value = 12;
        Assert.Equal((ulong)12, atomic.Value);

        Assert.Equal((ulong)13, atomic.Increment());
        Assert.Equal((ulong)12, atomic.Decrement());
        Assert.Equal((ulong)17, atomic.Add(5));
    }

    [Fact]
    public void Atomic_CompareAndSwap_ShouldReturnExpectedOutcome()
    {
        Atomic<ulong> atomic = new(20);

        bool first = atomic.CompareAndSwap(20, 30);
        bool second = atomic.CompareAndSwap(20, 40);

        Assert.True(first);
        Assert.False(second);
        Assert.Equal((ulong)30, atomic.Value);
    }
}
