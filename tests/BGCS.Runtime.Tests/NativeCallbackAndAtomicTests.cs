using System;
using BGCS.Runtime;
using Xunit;

namespace BGCS.Runtime.Tests;

public class NativeCallbackAndAtomicTests
{
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)]
    private delegate void RegistryCallback(int value);

    [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static int AotCallback(int value) => value + 1;

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
    public void NativeCallback_CopiedLease_ShouldDisposeExactlyOnce()
    {
        NativeCallback<Action> callback = new(() => { });
        NativeCallback<Action> copy = callback;

        callback.Dispose();
        copy.Dispose();

        Assert.True(callback.IsDisposed);
        Assert.True(copy.IsDisposed);
        Assert.False(callback.IsAllocated);
        Assert.False(copy.IsAllocated);
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
    public void NativeCallbackRegistry_ShouldReplaceUnregisterAndDisposeLeases()
    {
        using NativeCallbackRegistry<int, RegistryCallback> registry = new();
        RegistryCallback first = _ => { };
        RegistryCallback second = _ => { };

        nint firstPointer = registry.Register(7, first);
        nint secondPointer = registry.Register(7, second);

        Assert.NotEqual(0, firstPointer);
        Assert.NotEqual(0, secondPointer);
        Assert.Equal(1, registry.Count);
        Assert.True(registry.TryGet(7, out RegistryCallback? registered));
        Assert.Same(second, registered);
        Assert.True(registry.Unregister(7));
        Assert.False(registry.Unregister(7));
        Assert.Equal(0, registry.Count);
        registry.ReleaseRetired();
        registry.Dispose();
        Assert.Throws<ObjectDisposedException>(() => registry.Register(8, first));
    }

    [Fact]
    public unsafe void NativeAotCallback_ShouldExposeStaticUnmanagedThunk()
    {
        delegate* unmanaged[Cdecl]<int, int> callback = &AotCallback;

        nint pointer = NativeAotCallback.GetFunctionPointer(callback);

        Assert.NotEqual(0, pointer);
        Assert.Equal(8, callback(7));
    }

    [Fact]
    public void NativeCallbackExceptionBoundary_ShouldCaptureAndReturnFallback()
    {
        int result = NativeCallbackExceptionBoundary.Invoke<int>(() => throw new InvalidOperationException("callback failed"), -1);

        Assert.Equal(-1, result);
        InvalidOperationException exception = Assert.IsType<InvalidOperationException>(NativeCallbackExceptionBoundary.TakeLastException());
        Assert.Equal("callback failed", exception.Message);
        Assert.Null(NativeCallbackExceptionBoundary.TakeLastException());
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
