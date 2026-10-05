using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace BGCS.Runtime;

/// <summary>
/// Owns a retained native callback and coordinates synchronous unregistration with in-flight callback invocations.
/// </summary>
/// <remarks>
/// Native code must guarantee that the unregister operation returns only after it will start no new
/// invocations. Callback thunks call <see cref = "TryEnterInvocation"/> before touching managed state and dispose
/// the returned lease on exit. Disposal then waits for already-entered invocations before releasing the delegate.
/// </remarks>
/// <typeparam name="TDelegate">Managed delegate signature retained for the native registration.</typeparam>
public sealed class NativeCallbackRegistration<TDelegate> : IDisposable where TDelegate : Delegate
{
    private readonly object m_sync = new();
    private readonly ManualResetEventSlim m_drained = new(initialState: true);
    private readonly Action m_unregister;
    private NativeCallback<TDelegate> m_callback;
    private int m_activeInvocations;
    private bool m_closing;
    private bool m_unregistering;
    private bool m_unregisterCompleted;
    private bool m_releaseInProgress;
    private bool m_disposed;
    /// <summary>Creates and retains a callback registration.</summary>
    /// <param name = "callback">Managed callback exposed to native code.</param>
    /// <param name = "unregister">Synchronous native unregister operation.</param>
    public NativeCallbackRegistration(
        TDelegate callback,
        Action unregister
    ) {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(unregister);
        this.m_callback = new(callback);
        this.m_unregister = unregister;
        this.functionPointer = Marshal.GetFunctionPointerForDelegate(callback);
    }

    /// <summary>Gets the stable unmanaged callback pointer retained by this registration.</summary>
    public nint functionPointer { get; }

    /// <summary>Gets whether disposal has begun and new invocations are rejected.</summary>
    public bool isClosing
    {
        get
        {
            lock (this.m_sync)
                return this.m_closing;
        }
    }

    /// <summary>
    /// Acquires one in-flight invocation lease while registration remains open.
    /// </summary>
    /// <param name="lease">
    /// Receives the invocation lease on success, or null when closing has begun. Dispose it in a finally block.
    /// </param>
    /// <returns>
    /// True when the caller may enter managed callback state; false after closing begins.
    /// </returns>
    public bool TryEnterInvocation(out InvocationLease? lease)
    {
        lock (this.m_sync)
        {
            if (this.m_closing)
            {
                lease = null;
                return false;
            }

            this.m_activeInvocations++;
            this.m_drained.Reset();
            lease = new(this);
            return true;
        }
    }

    /// <summary>Synchronously unregisters, drains in-flight calls, and releases the delegate exactly once.</summary>
    public void Dispose()
    {
        bool performUnregister = false;
        lock (this.m_sync)
        {
            this.m_closing = true;
            while (this.m_unregistering)
                Monitor.Wait(this.m_sync);
            if (this.m_disposed)
                return;
            if (!this.m_unregisterCompleted)
            {
                this.m_unregistering = true;
                performUnregister = true;
            }
        }

        if (performUnregister)
        {
            try
            {
                this.m_unregister();
                lock (this.m_sync)
                    this.m_unregisterCompleted = true;
            }
            finally
            {
                lock (this.m_sync)
                {
                    this.m_unregistering = false;
                    Monitor.PulseAll(this.m_sync);
                }
            }
        }

        lock (this.m_sync)
        {
            while (!this.m_unregisterCompleted && this.m_unregistering)
                Monitor.Wait(this.m_sync);
            if (!this.m_unregisterCompleted)
                return;
            while (this.m_releaseInProgress && !this.m_disposed)
                Monitor.Wait(this.m_sync);
            if (this.m_disposed)
                return;
            this.m_releaseInProgress = true;
            if (this.m_activeInvocations == 0)
                this.m_drained.Set();
        }

        this.m_drained.Wait();
        lock (this.m_sync)
        {
            this.m_callback.Dispose();
            this.m_disposed = true;
            this.m_releaseInProgress = false;
            Monitor.PulseAll(this.m_sync);
        }

        this.m_drained.Dispose();
    }

    private void ExitInvocation()
    {
        lock (this.m_sync)
        {
            if (this.m_activeInvocations <= 0)
                throw new InvalidOperationException("Callback invocation lease was released more than once.");
            this.m_activeInvocations--;
            if (this.m_closing && this.m_activeInvocations == 0)
                this.m_drained.Set();
        }
    }

    /// <summary>One in-flight callback invocation.</summary>
    public sealed class InvocationLease : IDisposable
    {
        private NativeCallbackRegistration<TDelegate>? m_owner;
        internal InvocationLease(NativeCallbackRegistration<TDelegate> owner)
        {
            this.m_owner = owner;
        }

        /// <summary>Leaves the callback invocation. Repeated disposal is harmless.</summary>
        public void Dispose()
        {
            NativeCallbackRegistration<TDelegate>? current = Interlocked.Exchange(ref this.m_owner, null);
            current?.ExitInvocation();
        }
    }
}
