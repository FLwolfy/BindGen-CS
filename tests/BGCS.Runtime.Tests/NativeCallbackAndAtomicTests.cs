using System;
using BGCS.Runtime;
using Xunit;

namespace BGCS.Runtime.Tests;

using System.Threading;
using System.Threading.Tasks;

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
    public async Task NativeCallbackRegistration_DisposeShouldUnregisterThenDrainInFlightInvocation()
    {
        using ManualResetEventSlim entered = new();
        using ManualResetEventSlim release = new();
        int unregisterCalls = 0;
        NativeCallbackRegistration<RegistryCallback> registration = new(_ => { }, () => Interlocked.Increment(ref unregisterCalls));
        Task callback = Task.Run(() =>
        {
            Assert.True(registration.TryEnterInvocation(out NativeCallbackRegistration<RegistryCallback>.InvocationLease? lease));
            using (lease)
            {
                entered.Set();
                release.Wait();
            }
        });
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));

        Task dispose = Task.Run(registration.Dispose);
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref unregisterCalls) == 1, TimeSpan.FromSeconds(5)));
        Assert.False(dispose.IsCompleted);
        Assert.False(registration.TryEnterInvocation(out _));
        release.Set();

        await Task.WhenAll(callback, dispose);
        Assert.Equal(1, unregisterCalls);
        registration.Dispose();
        Assert.Equal(1, unregisterCalls);
    }

    [Fact]
    public async Task NativeAsyncOperation_ShouldRetainResourcesUntilExactlyOneTerminalSignal()
    {
        CountingDisposable first = new();
        CountingDisposable second = new();
        using NativeAsyncOperation<int> operation = new([first, second]);

        Assert.Equal(0, first.DisposeCount);
        Assert.True(operation.TrySetResult(42));
        Assert.False(operation.TrySetCanceled());
        Assert.Equal(42, await operation.Task);
        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(1, second.DisposeCount);
        operation.Dispose();
        Assert.Equal(1, first.DisposeCount);
    }

    [Fact]
    public async Task NativeAsyncOperation_CleanupFailureShouldFaultTerminalTaskWithoutLosingCompletion()
    {
        using NativeAsyncOperation<int> operation = new([new ThrowingDisposable()]);

        Assert.True(operation.TrySetResult(42));
        AggregateException exception = await Assert.ThrowsAsync<AggregateException>(async () => await operation.Task);
        Assert.Contains("failed to release", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(operation.TrySetException(new InvalidOperationException("late")));
    }

    [Fact]
    public void NativeCallbackRegistration_UnregisterFailureShouldRemainRetainedAndPermitRetry()
    {
        int attempts = 0;
        NativeCallbackRegistration<RegistryCallback> registration = new(_ => { }, () =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
                throw new InvalidOperationException("temporary unregister failure");
        });

        Assert.Throws<InvalidOperationException>(registration.Dispose);
        Assert.True(registration.IsClosing);
        Assert.False(registration.TryEnterInvocation(out _));
        registration.Dispose();
        registration.Dispose();
        Assert.Equal(2, attempts);
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

    private sealed class CountingDisposable : IDisposable
    {
        public int DisposeCount;
        public void Dispose() => Interlocked.Increment(ref DisposeCount);
    }

    private sealed class ThrowingDisposable : IDisposable
    {
        public void Dispose() => throw new InvalidOperationException("cleanup failed");
    }
}
