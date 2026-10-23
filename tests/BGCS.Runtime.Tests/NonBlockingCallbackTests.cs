using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace BGCS.Runtime.Tests;

public sealed class NonBlockingCallbackTests
{
    [Fact]
    public void CaptureWithoutContextRequiresAnExplicitConsumerQueue()
    {
        SynchronizationContext? previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(null);
            Assert.Throws<InvalidOperationException>(() => NativeCallbackDispatcher.CaptureCurrent());
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    [Fact]
    public void CaptureUsesTheProvidedContextWithoutSelectingAnOperatingSystemPolicy()
    {
        SynchronizationContext? previous = SynchronizationContext.Current;
        QueueContext context = new();
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            NativeCallbackDispatcher dispatcher = NativeCallbackDispatcher.CaptureCurrent();
            int calls = 0;
            Task queued = dispatcher.InvokeAsync(() => { calls++; return ValueTask.CompletedTask; }).AsTask();
            Assert.False(queued.IsCompleted);
            Assert.Equal(0, calls);
            context.Pump();
            Complete(queued);
            Assert.Equal(1, calls);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private sealed class QueueContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<Action> m_queue = new();
        public override void Post(
            SendOrPostCallback callback,
            object? state
        ) => m_queue.Enqueue(() => callback(state));
        internal void Pump() { while (m_queue.TryDequeue(out Action? action)) action(); }
    }
    [Fact]
    public void QueueRunsLaterOnItsCapturedThread()
    {
        Loop loop = new();
        int calls = 0, original = Environment.CurrentManagedThreadId;
        ValueTask queued = loop.dispatcher.InvokeAsync(() => { Assert.Equal(original, Environment.CurrentManagedThreadId); calls++; return ValueTask.CompletedTask; });
        Assert.False(queued.IsCompleted);
        Assert.Equal(0, calls);
        loop.Pump();
        Complete(queued.AsTask());
        Assert.Equal(1, calls);
    }

    [Fact]
    public void InlineQueueNeverExecutesNativeShutdown()
    {
        int calls = 0;
        NativeCallbackDispatcher dispatcher = new(action => action());
        Task queued = dispatcher.InvokeAsync(() => { calls++; return ValueTask.CompletedTask; }).AsTask();
        Assert.Throws<InvalidOperationException>(() => queued.GetAwaiter().GetResult());
        Assert.Equal(0, calls);
    }

    [Fact]
    public void WrongThreadQueueNeverExecutesNativeShutdown()
    {
        int calls = 0;
        NativeCallbackDispatcher dispatcher = new(action => { _ = Task.Run(action); });
        Task queued = dispatcher.InvokeAsync(() => { Interlocked.Increment(ref calls); return ValueTask.CompletedTask; }).AsTask();
        Assert.True(SpinWait.SpinUntil(() => queued.IsCompleted, TimeSpan.FromSeconds(3)));
        Assert.Throws<InvalidOperationException>(() => queued.GetAwaiter().GetResult());
        Assert.Equal(0, calls);
    }

    [Fact]
    public void DuplicateQueueDeliveryCannotRunShutdownTwice()
    {
        ConcurrentQueue<Action> queue = new();
        NativeCallbackDispatcher dispatcher = new(action => { queue.Enqueue(action); queue.Enqueue(action); });
        int calls = 0;
        Task task = dispatcher.InvokeAsync(() => { calls++; return ValueTask.CompletedTask; }).AsTask();
        while (queue.TryDequeue(out Action? action)) action();
        Complete(task);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void QueueFailureDoesNotLeaveAnExecutableNativeActionBehind()
    {
        Action? stored = null;
        NativeCallbackDispatcher dispatcher = new(action => { stored = action; throw new InvalidOperationException("queue-failure"); });
        int calls = 0;
        Task task = dispatcher.InvokeAsync(() => { calls++; return ValueTask.CompletedTask; }).AsTask();
        Assert.Equal("queue-failure", Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult()).Message);
        stored!();
        Assert.Equal(0, calls);
    }

    [Fact]
    public void SelfDisposalOnlyRequestsClosureUntilTheNativeFrameHasReturnedToTheLoop()
    {
        Loop loop = new();
        Probe state = new();
        int stops = 0;
        CallbackRegistration registration = new(1, () => { stops++; return ValueTask.CompletedTask; }, loop.dispatcher, state);
        Assert.True(registration.TryEnterInvocation(out var invocation));
        registration.Dispose();
        Assert.True(registration.isClosing);
        Assert.Equal(0, stops);
        Assert.Equal(0, state.releases);
        Assert.False(registration.completion.IsCompleted);
        invocation!.Dispose();
        Assert.Equal(0, stops);
        loop.Pump();
        Complete(registration.completion);
        Assert.Equal(1, stops);
        Assert.Equal(1, state.releases);
    }

    [Fact]
    public void AsyncAcknowledgementRetainsStateAndModuleAfterContextClosure()
    {
        Loop loop = new();
        int originalThread = Environment.CurrentManagedThreadId;
        Probe state = new(), provider = new();
        TaskCompletionSource acknowledge = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using NativeModuleSession session = new(new StaticModuleProvider([], provider.Dispose));
        CallbackRegistration registration = new(1, () => new(acknowledge.Task), loop.dispatcher, state, session.AcquireResource());
        Task close = registration.DisposeAsync().AsTask();
        session.Dispose();
        loop.Pump();
        Assert.False(close.IsCompleted);
        Assert.Equal(0, state.releases);
        Assert.Equal(0, provider.releases);
        acknowledge.SetResult();
        loop.PumpUntil(close);
        Assert.Equal(1, state.releases);
        Assert.Equal(1, provider.releases);
        Assert.Equal(originalThread, state.releaseThread);
        Assert.Equal(originalThread, provider.releaseThread);
    }

    [Fact]
    public void PublicationAndUseDrainMustCompleteBeforeNativeWorkIsQueued()
    {
        Loop loop = new();
        TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        Task work = loop.dispatcher.InvokeAfterAsync(ready.Task, () => calls++);
        loop.Pump();
        Assert.Equal(0, calls);
        Assert.False(work.IsCompleted);
        ready.SetResult();
        loop.PumpUntil(work);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void FailedShutdownCannotPublishASuccessfulTerminalResult()
    {
        Loop loop = new();
        int terminal = 0;
        Exception? observed = null;
        InvalidOperationException failure = new("native-shutdown-failure");
        Task work = loop.dispatcher.ContinueAsync(ValueTask.FromException(failure), () => terminal++, error => observed = error);
        loop.PumpUntil(work);
        Assert.Same(failure, observed);
        Assert.Equal(0, terminal);
    }

    [Fact]
    public void FailedNonblockingUnregisterRetainsResourcesAndCanBeRetried()
    {
        Loop loop = new();
        Probe state = new();
        int attempts = 0;
        CallbackRegistration registration = new(1, () => ++attempts == 1 ? ValueTask.FromException(new InvalidOperationException("stop-failure")) : ValueTask.CompletedTask,
            loop.dispatcher, state);
        Task first = registration.DisposeAsync().AsTask();
        loop.Pump();
        Assert.True(SpinWait.SpinUntil(() => first.IsCompleted, TimeSpan.FromSeconds(3)));
        Assert.Equal("stop-failure", Assert.Throws<InvalidOperationException>(() => first.GetAwaiter().GetResult()).Message);
        Assert.NotNull(registration.closeFailure);
        Assert.False(registration.completion.IsCompleted);
        Assert.Equal(0, state.releases);
        Task second = registration.DisposeAsync().AsTask();
        loop.Pump();
        Complete(second);
        Assert.Null(registration.closeFailure);
        Assert.Equal(2, attempts);
        Assert.Equal(1, state.releases);
    }

    [Fact]
    public void EventLoopRegistrationRefusesForeignThreadInvocationAdmission()
    {
        Loop loop = new();
        CallbackRegistration registration = new(1, () => ValueTask.CompletedTask, loop.dispatcher);
        Task denied = Task.Run(() => Assert.Throws<InvalidOperationException>(() => registration.TryEnterInvocation(out _)));
        Complete(denied);
        registration.Dispose();
        loop.Pump();
        Complete(registration.completion);
    }

    private static void Complete(Task task)
    {
        Assert.True(SpinWait.SpinUntil(() => task.IsCompleted, TimeSpan.FromSeconds(3)), "Nonblocking completion did not settle.");
        task.GetAwaiter().GetResult();
    }

    private sealed class Loop
    {
        private readonly ConcurrentQueue<Action> m_queue = new();
        internal NativeCallbackDispatcher dispatcher { get; }
        internal Loop() => dispatcher = new(m_queue.Enqueue);
        internal void Pump() { while (m_queue.TryDequeue(out Action? action)) action(); }
        internal void PumpUntil(Task task)
        {
            Assert.True(SpinWait.SpinUntil(() => { Pump(); return task.IsCompleted; }, TimeSpan.FromSeconds(3)));
            task.GetAwaiter().GetResult();
        }
    }

    private sealed class Probe : IDisposable
    {
        private int m_releases;
        private int m_releaseThread;
        internal int releases => Volatile.Read(ref m_releases);
        internal int releaseThread => Volatile.Read(ref m_releaseThread);
        public void Dispose() { Volatile.Write(ref m_releaseThread, Environment.CurrentManagedThreadId); Interlocked.Increment(ref m_releases); }
    }
}
