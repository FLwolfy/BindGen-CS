using System;
using Xunit;

namespace BGCS.Runtime.Tests;

using System.Threading;
using System.Threading.Tasks;

public class NativeCallbackTests
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

        Assert.False(callback.isNull);
        Assert.True(callback.isAllocated);

        callback.Dispose();

        Assert.True(callback.isNull);
        Assert.False(callback.isAllocated);
    }

    [Fact]
    public void NativeCallback_CopiedLease_ShouldDisposeExactlyOnce()
    {
        NativeCallback<Action> callback = new(() => { });
        NativeCallback<Action> copy = callback;

        callback.Dispose();
        copy.Dispose();

        Assert.True(callback.isDisposed);
        Assert.True(copy.isDisposed);
        Assert.False(callback.isAllocated);
        Assert.False(copy.isAllocated);
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
        Assert.Equal(1, registry.count);
        Assert.True(registry.TryGet(7, out RegistryCallback? registered));
        Assert.Same(second, registered);
        Assert.True(registry.Unregister(7));
        Assert.False(registry.Unregister(7));
        Assert.Equal(0, registry.count);
        registry.ReleaseRetired();
        registry.Dispose();
        Assert.Throws<ObjectDisposedException>(() => registry.Register(8, first));
    }

    [Fact]
    public async Task CallbackRegistration_DisposeShouldUnregisterThenDrainInFlightInvocation()
    {
        using ManualResetEventSlim entered = new();
        using ManualResetEventSlim release = new();
        int unregisterCalls = 0;
        CallbackRegistration<RegistryCallback> registration = new(_ => { }, () => Interlocked.Increment(ref unregisterCalls));
        Task callback = Task.Run(() =>
        {
            Assert.True(registration.TryEnterInvocation(out CallbackRegistration<RegistryCallback>.InvocationLease? lease));
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
    public async Task NativeOperation_ShouldRetainResourcesUntilExactlyOneTerminalSignal()
    {
        CountingDisposable first = new();
        CountingDisposable second = new();
        using NativeOperation<int> operation = new([first, second]);

        Assert.Equal(0, first.DisposeCount);
        Assert.True(operation.TrySetResult(42));
        Assert.False(operation.TrySetCanceled());
        Assert.Equal(42, await operation.task);
        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(1, second.DisposeCount);
        operation.Dispose();
        Assert.Equal(1, first.DisposeCount);
    }

    [Fact]
    public async Task NativeOperation_CleanupFailureShouldFaultTerminalTaskWithoutLosingCompletion()
    {
        using NativeOperation<int> operation = new([new ThrowingDisposable()]);

        Assert.True(operation.TrySetResult(42));
        AggregateException exception = await Assert.ThrowsAsync<AggregateException>(async () => await operation.task);
        Assert.Contains("failed to release", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(operation.TrySetException(new InvalidOperationException("late")));
    }

    [Fact]
    public void CallbackRegistration_UnregisterFailureShouldRemainRetainedAndPermitRetry()
    {
        int attempts = 0;
        CallbackRegistration<RegistryCallback> registration = new(_ => { }, () =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
                throw new InvalidOperationException("temporary unregister failure");
        });

        Assert.Throws<InvalidOperationException>(registration.Dispose);
        Assert.True(registration.isClosing);
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
    public void CallbackExceptionBoundary_ShouldCaptureAndReturnFallback()
    {
        CallbackExceptionBoundary boundary = new();
        int result = boundary.Invoke<int>(() => throw new InvalidOperationException("callback failed"), -1);

        Assert.Equal(-1, result);
        InvalidOperationException exception = Assert.IsType<InvalidOperationException>(boundary.TakeException());
        Assert.Equal("callback failed", exception.Message);
        Assert.Null(boundary.TakeException());
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
