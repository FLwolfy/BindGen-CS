using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace BGCS.Runtime;

/// <summary>Specifies when managed callback admission closes relative to native unregister and join.</summary>
public enum CallbackShutdownOrder
{
    /// <summary>Rejects new managed invocations as soon as shutdown is requested.</summary>
    CloseBeforeUnregister,
    /// <summary>Admits callbacks until native unregister has stopped and joined every native execution frame.</summary>
    KeepOpenUntilUnregistered
}

/// <summary>Retains callback state and its module until native unregistration and all entered invocations finish.</summary>
/// <remarks>The native unregister action must prevent new invocations before returning. When this registration owns a module lease, it must also synchronize any native execution that would otherwise outlive that lease. Blocking self-disposal dispatches unregister outside the callback thread; the nonblocking overload queues cleanup on a later event-loop turn. A failed unregister retains all resources and can be retried.</remarks>
public class CallbackRegistration : IDisposable, IAsyncDisposable
{
    private readonly object m_gate = new();
    private readonly Dictionary<int, int> m_invokingThreads = [];
    private readonly Action m_unregister;
    private readonly Func<ValueTask>? m_unregisterAsync;
    private readonly NativeCallbackDispatcher? m_dispatcher;
    private readonly IDisposable? m_retainedState;
    private readonly NativeModuleLease? m_module;
    private readonly CallbackShutdownOrder m_shutdownOrder;
    private readonly TaskCompletionSource m_released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource? m_unregisterAttempt;
    private int m_activeInvocations;
    private bool m_closing;
    private bool m_unregistered;
    private bool m_releasing;
    private int m_unregisterThread;
    private Exception? m_closeFailure;

    /// <summary>Takes ownership of retained state and an optional module lease after validating a callback pointer.</summary>
    /// <param name="functionPointer">A static AOT thunk or another explicitly retained nonzero callback address.</param>
    /// <param name="unregister">Native action that prevents new callback invocations before returning.</param>
    /// <param name="retainedState">Optional callback state released after the final invocation.</param>
    /// <param name="module">Optional module lease transferred to this registration.</param>
    /// <param name="shutdownOrder">Whether callbacks remain admitted while the native stop-and-join action executes.</param>
    /// <exception cref="ArgumentException">The callback address is zero.</exception>
    /// <exception cref="ArgumentNullException">The unregister action is null.</exception>
    public CallbackRegistration(
        nint functionPointer,
        Action unregister,
        IDisposable? retainedState = null,
        NativeModuleLease? module = null,
        CallbackShutdownOrder shutdownOrder = CallbackShutdownOrder.CloseBeforeUnregister
    ) {
        if (functionPointer == 0)
            throw new ArgumentException("A callback requires a nonzero address.", nameof(functionPointer));
        ArgumentNullException.ThrowIfNull(unregister);
        if (!Enum.IsDefined(shutdownOrder)) throw new ArgumentOutOfRangeException(nameof(shutdownOrder));
        this.functionPointer = functionPointer;
        m_unregister = unregister;
        m_retainedState = retainedState;
        m_module = module;
        m_shutdownOrder = shutdownOrder;
    }

    /// <summary>Takes ownership of callback resources for an explicitly nonblocking, event-loop-affine native shutdown protocol.</summary>
    /// <param name="functionPointer">The retained nonzero callback address.</param>
    /// <param name="unregisterAsync">Awaits publication and entered native uses, then establishes full native quiescence without blocking.</param>
    /// <param name="dispatcher">The captured or injected queue that runs after native frames return.</param>
    /// <param name="retainedState">State released only after unregister and all entered invocations finish.</param>
    /// <param name="module">The module lease adopted after constructor validation succeeds.</param>
    /// <param name="shutdownOrder">The declared callback admission order during native shutdown.</param>
    /// <remarks>Dispose requests closure and returns; DisposeAsync and completion observe native quiescence and resource release. A failed queue or unregister retains resources for a later retry.</remarks>
    public CallbackRegistration(
        nint functionPointer,
        Func<ValueTask> unregisterAsync,
        NativeCallbackDispatcher dispatcher,
        IDisposable? retainedState = null,
        NativeModuleLease? module = null,
        CallbackShutdownOrder shutdownOrder = CallbackShutdownOrder.CloseBeforeUnregister
    )
        : this(functionPointer, static () => { }, retainedState, module, shutdownOrder)
    {
        ArgumentNullException.ThrowIfNull(unregisterAsync);
        ArgumentNullException.ThrowIfNull(dispatcher);
        m_unregisterAsync = unregisterAsync;
        m_dispatcher = dispatcher;
    }

    /// <summary>Gets the callback address retained until closure completes.</summary>
    public nint functionPointer { get; }

    /// <summary>Gets whether shutdown was requested; the selected stop-and-join policy controls callback admission.</summary>
    public bool isClosing
    {
        get { lock (m_gate) return m_closing; }
    }

    /// <summary>Gets the nonblocking completion of native unregister and resource drain.</summary>
    public Task completion => m_released.Task;

    /// <summary>Gets the last failed native unregister attempt while its resources remain retained.</summary>
    public Exception? closeFailure
    {
        get { lock (m_gate) return m_closeFailure; }
    }

    /// <summary>Enters managed callback state while the registration remains open.</summary>
    /// <param name="lease">An invocation lease to release in the thunk's finally block.</param>
    /// <returns>False after closure begins; otherwise true with a retained invocation.</returns>
    public bool TryEnterInvocation(out InvocationLease? lease)
    {
        m_dispatcher?.VerifyAccess();
        lock (m_gate)
        {
            if (m_closing && (m_shutdownOrder == CallbackShutdownOrder.CloseBeforeUnregister || m_unregistered))
            {
                lease = null;
                return false;
            }
            NativeCallLease? call = m_module?.AcquireCleanupCall();
            int thread = Environment.CurrentManagedThreadId;
            m_activeInvocations++;
            m_invokingThreads.TryGetValue(thread, out int count);
            m_invokingThreads[thread] = count + 1;
            lease = new(this, thread, call);
            return true;
        }
    }

    /// <summary>Unregisters and drains callbacks; self-disposal defers final release until this invocation exits.</summary>
    /// <remarks>Use DisposeAsync in environments where waiting on another invocation would block its execution.</remarks>
    public void Dispose()
    {
        bool self;
        lock (m_gate)
            self = m_invokingThreads.ContainsKey(Environment.CurrentManagedThreadId) || m_unregisterThread == Environment.CurrentManagedThreadId;
        Task unregister = BeginUnregister();
        if (m_unregisterAsync is not null) return;
        if (self)
        {
            if (unregister.IsCompleted)
                unregister.GetAwaiter().GetResult();
            return;
        }
        unregister.GetAwaiter().GetResult();
        m_released.Task.GetAwaiter().GetResult();
    }

    /// <summary>Requests closure and asynchronously waits for all invocation resources.</summary>
    /// <returns>A closure task. Await it outside an active invocation of this registration.</returns>
    public async ValueTask DisposeAsync()
    {
        await BeginUnregister().ConfigureAwait(false);
        await m_released.Task.ConfigureAwait(false);
    }

    private Task BeginUnregister()
    {
        TaskCompletionSource attempt;
        bool dispatch;
        lock (m_gate)
        {
            m_closing = true;
            if (m_unregistered)
                return Task.CompletedTask;
            if (m_unregisterAttempt is not null)
                return m_unregisterAttempt.Task;
            attempt = new(TaskCreationOptions.RunContinuationsAsynchronously);
            m_unregisterAttempt = attempt;
            dispatch = m_invokingThreads.ContainsKey(Environment.CurrentManagedThreadId);
        }
        if (m_unregisterAsync is not null)
            _ = DispatchUnregisterAsync(attempt);
        else if (dispatch)
            _ = Task.Run(() => ExecuteUnregister(attempt));
        else
            ExecuteUnregister(attempt);
        return attempt.Task;
    }

    private async Task DispatchUnregisterAsync(TaskCompletionSource attempt)
    {
        try
        {
            await m_dispatcher!.InvokeAsync(async () =>
            {
                using (NativeCallLease? cleanup = m_module?.AcquireCleanupCall())
                    await m_unregisterAsync!().ConfigureAwait(false);
                await m_dispatcher.InvokeAsync(() =>
                {
                    lock (m_gate) { m_unregistered = true; m_closeFailure = null; }
                    attempt.TrySetResult();
                    ReleaseIfDrained();
                    return ValueTask.CompletedTask;
                }).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            lock (m_gate) { m_unregisterAttempt = null; m_closeFailure = failure; }
            attempt.TrySetException(failure);
        }
    }

    private void ExecuteUnregister(TaskCompletionSource attempt)
    {
        try
        {
            lock (m_gate)
                m_unregisterThread = Environment.CurrentManagedThreadId;
            using (NativeCallLease? cleanup = m_module?.AcquireCleanupCall())
                m_unregister();
            lock (m_gate)
            {
                m_unregistered = true;
                m_closeFailure = null;
            }
            attempt.TrySetResult();
            ReleaseIfDrained();
        }
        catch (Exception exception)
        {
            lock (m_gate)
            {
                m_unregisterAttempt = null;
                m_closeFailure = exception;
            }
            attempt.TrySetException(exception);
        }
        finally
        {
            lock (m_gate)
                m_unregisterThread = 0;
        }
    }

    private void ExitInvocation(
        int thread,
        NativeCallLease? call
    ) {
        lock (m_gate)
        {
            m_activeInvocations--;
            if (--m_invokingThreads[thread] == 0)
                m_invokingThreads.Remove(thread);
        }
        call?.Dispose();
        ReleaseIfDrained();
    }

    private void ReleaseIfDrained()
    {
        lock (m_gate)
        {
            if (!m_unregistered || m_activeInvocations != 0 || m_releasing)
                return;
            m_releasing = true;
        }
        try
        {
            m_retainedState?.Dispose();
            m_module?.Dispose();
            m_released.TrySetResult();
        }
        catch (Exception exception)
        {
            // Keep the module if retained-state cleanup fails: native code may still need it.
            m_released.TrySetException(exception);
        }
    }

    /// <summary>Retains one entered callback invocation and its native module.</summary>
    public sealed class InvocationLease : IDisposable
    {
        private CallbackRegistration? m_owner;
        private readonly int m_thread;
        private readonly NativeCallLease? m_call;

        internal InvocationLease(
            CallbackRegistration owner,
            int thread,
            NativeCallLease? call
        ) {
            m_owner = owner;
            m_thread = thread;
            m_call = call;
        }

        /// <summary>Leaves the invocation exactly once.</summary>
        public void Dispose() => Interlocked.Exchange(ref m_owner, null)?.ExitInvocation(m_thread, m_call);
    }
}

/// <summary>Retains a delegate-backed callback using the same unregister and invocation protocol as static AOT thunks.</summary>
/// <typeparam name="TDelegate">The callback delegate signature.</typeparam>
public sealed class CallbackRegistration<TDelegate> : CallbackRegistration where TDelegate : Delegate
{
    /// <summary>Creates a delegate-backed registration and optionally transfers a module lease.</summary>
    /// <param name="callback">The callback delegate kept alive for native calls.</param>
    /// <param name="unregister">Native action that prevents new callback starts before returning.</param>
    /// <param name="module">Optional module resource lease transferred on successful construction.</param>
    /// <param name="shutdownOrder">Whether callback admission remains open until native stop-and-join completes.</param>
    public CallbackRegistration(
        TDelegate callback,
        Action unregister,
        NativeModuleLease? module = null,
        CallbackShutdownOrder shutdownOrder = CallbackShutdownOrder.CloseBeforeUnregister
    )
        : this(CreateDelegateState(callback, unregister, shutdownOrder), unregister, module, shutdownOrder) { }

    private CallbackRegistration(
        DelegateState state,
        Action unregister,
        NativeModuleLease? module,
        CallbackShutdownOrder shutdownOrder
    )
        : base(state.pointer, unregister, state, module, shutdownOrder) { }

    private static DelegateState CreateDelegateState(
        TDelegate callback,
        Action unregister,
        CallbackShutdownOrder shutdownOrder
    ) {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(unregister);
        if (!Enum.IsDefined(shutdownOrder)) throw new ArgumentOutOfRangeException(nameof(shutdownOrder));
        return new(callback);
    }

    private sealed class DelegateState : IDisposable
    {
        private readonly NativeCallback<TDelegate> m_callback;
        internal readonly nint pointer;
        internal DelegateState(TDelegate callback)
        {
            pointer = Marshal.GetFunctionPointerForDelegate(callback);
            m_callback = new(callback);
        }
        public void Dispose() => m_callback.Dispose();
    }
}
