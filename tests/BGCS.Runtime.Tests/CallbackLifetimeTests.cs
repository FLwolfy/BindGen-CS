using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace BGCS.Runtime.Tests;

public sealed class CallbackLifetimeTests
{
    [Fact]
    public async Task SelfDisposal_DefersReleaseUntilInvocationLeaves()
    {
        ProbeState state = new();
        int unregisters = 0;
        int callbackThread = Environment.CurrentManagedThreadId;
        CallbackRegistration registration = new(1, () =>
        {
            Assert.NotEqual(callbackThread, Environment.CurrentManagedThreadId);
            Interlocked.Increment(ref unregisters);
        }, state);
        Assert.True(registration.TryEnterInvocation(out var invocation));
        registration.Dispose();
        Assert.Equal(0, state.releases);
        Assert.False(registration.completion.IsCompleted);
        invocation!.Dispose();
        await registration.completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, unregisters);
        Assert.Equal(1, state.releases);
    }

    [Fact]
    public async Task AsyncClosure_DrainsWithoutBlockingTheCallbackThread()
    {
        ProbeState state = new();
        CallbackRegistration registration = new(1, () => { }, state);
        Assert.True(registration.TryEnterInvocation(out var invocation));
        Task close = registration.DisposeAsync().AsTask();
        Assert.False(close.IsCompleted);
        Assert.False(registration.TryEnterInvocation(out _));
        invocation!.Dispose();
        await close.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, state.releases);
    }

    [Fact]
    public async Task ContextClosure_RetainsUnregisterAndEnteredCallbackModule()
    {
        ProbeState providerState = new();
        using NativeModuleSession session = new(new StaticModuleProvider([], providerState.Dispose));
        CallbackRegistration registration = new(1, () => Assert.Equal(0, providerState.releases), module: session.AcquireResource());
        session.Dispose();
        Assert.True(registration.TryEnterInvocation(out var invocation));
        Task close = registration.DisposeAsync().AsTask();
        Assert.Equal(0, providerState.releases);
        invocation!.Dispose();
        await close.WaitAsync(TimeSpan.FromSeconds(5));
        await session.DisposeAsync();
        Assert.Equal(1, providerState.releases);
    }

    [Fact]
    public async Task ConcurrentClosure_UnregistersAndReleasesOnce()
    {
        ProbeState state = new();
        int unregisters = 0;
        CallbackRegistration registration = new(1, () => Interlocked.Increment(ref unregisters), state);
        await Task.WhenAll(Task.Run(registration.Dispose), Task.Run(registration.Dispose), registration.DisposeAsync().AsTask());
        Assert.Equal(1, unregisters);
        Assert.Equal(1, state.releases);
    }

    [Fact]
    public async Task NativeStopAndJoinCanInvokeCallbacksUntilItAcknowledgesQuiescence()
    {
        ProbeState state = new();
        ProbeState provider = new();
        using NativeModuleSession session = new(new StaticModuleProvider([], provider.Dispose));
        using ManualResetEventSlim joining = new(), completeJoin = new();
        CallbackRegistration? registration = null;
        registration = new(1, () =>
        {
            joining.Set();
            Assert.True(completeJoin.Wait(TimeSpan.FromSeconds(10)));
            Assert.True(registration!.TryEnterInvocation(out var shutdownCallback));
            shutdownCallback!.Dispose();
        }, state, session.AcquireResource(), CallbackShutdownOrder.KeepOpenUntilUnregistered);
        session.Dispose();
        Task closing = Task.Run(registration.Dispose);
        Assert.True(joining.Wait(TimeSpan.FromSeconds(10)));
        Assert.True(registration.isClosing);
        Assert.True(registration.TryEnterInvocation(out var pending));
        Assert.Equal(0, provider.releases);
        completeJoin.Set();
        // Native unregister has returned, but an entered managed callback still
        // owns its resources. Late invocations must now be rejected.
        await WaitUntil(() => !registration.TryEnterInvocation(out var probe) || DisposeProbe(probe));
        Assert.False(closing.IsCompleted);
        pending!.Dispose();
        await closing.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, state.releases);
        Assert.Equal(1, provider.releases);
        Assert.False(registration.TryEnterInvocation(out _));

        static bool DisposeProbe(CallbackRegistration.InvocationLease? probe) { probe!.Dispose(); return false; }
        static async Task WaitUntil(Func<bool> condition)
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
            while (!condition()) await Task.Delay(1, timeout.Token);
        }
    }

    [Fact]
    public async Task FailedNativeJoinKeepsCallbackAdmissionAndModuleUntilSuccessfulRetry()
    {
        ProbeState provider = new(), state = new();
        using NativeModuleSession session = new(new StaticModuleProvider([], provider.Dispose));
        int attempts = 0;
        CallbackRegistration registration = new(1, () =>
        {
            if (Interlocked.Increment(ref attempts) == 1) throw new InvalidOperationException("Native execution has not quiesced.");
        }, state, session.AcquireResource(), CallbackShutdownOrder.KeepOpenUntilUnregistered);
        session.Dispose();
        await Assert.ThrowsAsync<InvalidOperationException>(() => registration.DisposeAsync().AsTask());
        Assert.Equal(0, provider.releases);
        Assert.Equal(0, state.releases);
        Assert.True(registration.TryEnterInvocation(out var invocation));
        invocation!.Dispose();
        await registration.DisposeAsync();
        Assert.Equal(2, attempts);
        Assert.Null(registration.closeFailure);
        Assert.Equal(1, provider.releases);
        Assert.Equal(1, state.releases);
        Assert.False(registration.TryEnterInvocation(out _));
    }

    [Fact]
    public async Task CleanupFailure_RetainsModuleAndReportsFailure()
    {
        ProbeState providerState = new();
        using NativeModuleSession session = new(new StaticModuleProvider([], providerState.Dispose));
        NativeModuleLease module = session.AcquireResource();
        CallbackRegistration registration = new(1, () => { }, new FaultingState(), module);
        Task closing = session.DisposeAsync().AsTask();
        Assert.False(closing.IsCompleted);
        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(() => registration.DisposeAsync().AsTask());
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => closing.WaitAsync(TimeSpan.FromSeconds(5))));
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5))));
        Assert.Equal(0, providerState.releases);
        module.Dispose();
        Assert.Equal(0, providerState.releases);
    }

    private sealed class ProbeState : IDisposable
    {
        internal int releases;
        public void Dispose() => Interlocked.Increment(ref releases);
    }

    private sealed class FaultingState : IDisposable
    {
        public void Dispose() => throw new InvalidOperationException("Native callback state could not be released.");
    }

    [Fact]
    public async Task SelfDisposal_AllowsNativeStopToWaitForItsCallbackReturn()
    {
        using ManualResetEventSlim nativeReturned = new();
        ProbeState provider = new();
        NativeModuleSession session = new(new StaticModuleProvider([], provider.Dispose));
        CallbackRegistration registration = new(1, () => Assert.True(nativeReturned.Wait(TimeSpan.FromSeconds(5))), module: session.AcquireResource());
        Assert.True(registration.TryEnterInvocation(out var invocation));
        session.Dispose();
        registration.Dispose();
        invocation!.Dispose();
        Assert.Equal(0, provider.releases);
        nativeReturned.Set();
        await registration.completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, provider.releases);
    }
}
