using System;
using System.Threading;
using System.Threading.Tasks;

namespace BGCS.Runtime;

/// <summary>Queues native lifecycle operations to a captured event-loop thread after the current native invocation returns.</summary>
/// <remarks>The supplied queue must never execute inline or pump a queued operation inside an active native frame. BGCS checks inline execution and thread affinity; the caller remains responsible for an injected queue's event-loop contract.</remarks>
public sealed class NativeCallbackDispatcher
{
    private readonly Action<Action> m_enqueue;
    private readonly int m_thread = Environment.CurrentManagedThreadId;

    /// <summary>Captures the current thread and an explicitly supplied event-loop enqueue operation.</summary>
    /// <param name="enqueue">A queue that executes later on this thread, after its native frames have returned.</param>
    public NativeCallbackDispatcher(Action<Action> enqueue)
    {
        ArgumentNullException.ThrowIfNull(enqueue);
        m_enqueue = enqueue;
    }

    /// <summary>Captures the current synchronization context, or the single-thread browser runtime's asynchronous background queue.</summary>
    /// <returns>A dispatcher whose queued invocations are checked for inline execution and original-thread affinity.</returns>
    /// <exception cref="InvalidOperationException">No captured context or browser event-loop queue is available.</exception>
    public static NativeCallbackDispatcher CaptureCurrent()
    {
        SynchronizationContext? context = SynchronizationContext.Current;
        if (context is not null) return new(action => context.Post(static state => ((Action)state!)(), action));
        if (!OperatingSystem.IsBrowser())
            throw new InvalidOperationException("Nonblocking native callback cleanup requires a captured event-loop synchronization context or an explicitly injected dispatcher.");
        // The .NET 10 single-thread browser runtime schedules background work on a
        // later JS event-loop turn (runtime/v10.0.12/src/mono/browser/runtime/scheduling.ts).
        // A threaded runtime without an affine context is still checked by VerifyAccess.
        return new(action => ThreadPool.QueueUserWorkItem(static (Action work) => work(), action, preferLocal: false));
    }

    /// <summary>Refuses direct native use from a thread other than the captured event-loop thread.</summary>
    public void VerifyAccess()
    {
        if (Environment.CurrentManagedThreadId != m_thread)
            throw new InvalidOperationException("This native callback protocol is confined to its captured event-loop thread.");
    }

    /// <summary>Queues an asynchronous lifecycle operation without blocking the calling thread.</summary>
    /// <param name="operation">An operation that preserves native lifetime resources through its awaited completion.</param>
    /// <returns>Completion or queue/protocol failure; an invalid queue never executes the native operation.</returns>
    public ValueTask InvokeAsync(Func<ValueTask> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int returned = 0, entered = 0;
        try
        {
            m_enqueue(() =>
            {
                if (Interlocked.Exchange(ref entered, 1) != 0 || completion.Task.IsCompleted) return;
                try
                {
                    if (Volatile.Read(ref returned) == 0)
                        throw new InvalidOperationException("A native callback dispatcher executed inline; shutdown must run after the current native frame returns.");
                    VerifyAccess();
                    _ = RunAsync();
                }
                catch (Exception failure) { completion.TrySetException(failure); }
            });
        }
        catch (Exception failure) { completion.TrySetException(failure); }
        finally { Volatile.Write(ref returned, 1); }
        return new(completion.Task);

        async Task RunAsync()
        {
            try { await operation().ConfigureAwait(false); completion.TrySetResult(); }
            catch (Exception failure) { completion.TrySetException(failure); }
        }
    }

    /// <summary>Awaits readiness without blocking, then queues native work on a later event-loop turn.</summary>
    /// <param name="prerequisite">Publication and entered-use drain completion.</param>
    /// <param name="operation">The native operation to execute on the captured thread.</param>
    /// <returns>Completion or the original prerequisite, queue or operation failure.</returns>
    public async Task InvokeAfterAsync(
        Task prerequisite,
        Action operation
    ) {
        ArgumentNullException.ThrowIfNull(prerequisite);
        ArgumentNullException.ThrowIfNull(operation);
        await prerequisite.ConfigureAwait(false);
        await InvokeAsync(() => { operation(); return ValueTask.CompletedTask; }).ConfigureAwait(false);
    }

    /// <summary>Awaits native shutdown before queuing terminal publication on its event-loop thread.</summary>
    /// <param name="prerequisite">Complete native shutdown and retained-resource drain.</param>
    /// <param name="complete">An action run only after successful shutdown.</param>
    /// <param name="failure">A failure observer that must not assume native resource access has ended.</param>
    /// <returns>The observed continuation, including an exception thrown by the failure observer.</returns>
    public async Task ContinueAsync(
        ValueTask prerequisite,
        Action complete,
        Action<Exception> failure
    ) {
        ArgumentNullException.ThrowIfNull(complete);
        ArgumentNullException.ThrowIfNull(failure);
        try { await prerequisite.ConfigureAwait(false); await InvokeAfterAsync(Task.CompletedTask, complete).ConfigureAwait(false); }
        catch (Exception error) { failure(error); }
    }
}
