using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace BGCS.Runtime;

/// <summary>
/// Owns a retained native callback and coordinates synchronous unregistration with in-flight callback invocations.
/// </summary>
/// <remarks>
/// Native code must guarantee that <paramref name="unregister"/> returns only after it will start no new
/// invocations. Callback thunks call <see cref="TryEnterInvocation"/> before touching managed state and dispose
/// the returned lease on exit. Disposal then waits for already-entered invocations before releasing the delegate.
/// </remarks>
public sealed class NativeCallbackRegistration<TDelegate> : IDisposable where TDelegate : Delegate
{
    private readonly object sync = new();
    private readonly ManualResetEventSlim drained = new(initialState: true);
    private readonly Action unregister;
    private NativeCallback<TDelegate> callback;
    private int activeInvocations;
    private bool closing;
    private bool unregistering;
    private bool unregisterCompleted;
    private bool releaseInProgress;
    private bool disposed;

    /// <summary>Creates and retains a callback registration.</summary>
    /// <param name="callback">Managed callback exposed to native code.</param>
    /// <param name="unregister">Synchronous native unregister operation.</param>
    public NativeCallbackRegistration(TDelegate callback, Action unregister)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(unregister);
        this.callback = new(callback);
        this.unregister = unregister;
        FunctionPointer = Marshal.GetFunctionPointerForDelegate(callback);
    }

    /// <summary>Gets the stable unmanaged callback pointer retained by this registration.</summary>
    public nint FunctionPointer { get; }

    /// <summary>Gets whether disposal has begun and new invocations are rejected.</summary>
    public bool IsClosing
    {
        get
        {
            lock (sync)
                return closing;
        }
    }

    /// <summary>
    /// Enters an invocation while the registration is active. The lease must be disposed in a finally block.
    /// </summary>
    public bool TryEnterInvocation(out InvocationLease? lease)
    {
        lock (sync)
        {
            if (closing)
            {
                lease = null;
                return false;
            }
            activeInvocations++;
            drained.Reset();
            lease = new(this);
            return true;
        }
    }

    /// <summary>Synchronously unregisters, drains in-flight calls, and releases the delegate exactly once.</summary>
    public void Dispose()
    {
        bool performUnregister = false;
        lock (sync)
        {
            closing = true;
            while (unregistering)
                Monitor.Wait(sync);
            if (disposed)
                return;
            if (!unregisterCompleted)
            {
                unregistering = true;
                performUnregister = true;
            }
        }

        if (performUnregister)
        {
            try
            {
                unregister();
                lock (sync)
                    unregisterCompleted = true;
            }
            finally
            {
                lock (sync)
                {
                    unregistering = false;
                    Monitor.PulseAll(sync);
                }
            }
        }

        lock (sync)
        {
            while (!unregisterCompleted && unregistering)
                Monitor.Wait(sync);
            if (!unregisterCompleted)
                return;
            while (releaseInProgress && !disposed)
                Monitor.Wait(sync);
            if (disposed)
                return;
            releaseInProgress = true;
            if (activeInvocations == 0)
                drained.Set();
        }
        drained.Wait();
        lock (sync)
        {
            callback.Dispose();
            disposed = true;
            releaseInProgress = false;
            Monitor.PulseAll(sync);
        }
        drained.Dispose();
    }

    private void ExitInvocation()
    {
        lock (sync)
        {
            if (activeInvocations <= 0)
                throw new InvalidOperationException("Callback invocation lease was released more than once.");
            activeInvocations--;
            if (closing && activeInvocations == 0)
                drained.Set();
        }
    }

    /// <summary>One in-flight callback invocation.</summary>
    public sealed class InvocationLease : IDisposable
    {
        private NativeCallbackRegistration<TDelegate>? owner;

        internal InvocationLease(NativeCallbackRegistration<TDelegate> owner)
        {
            this.owner = owner;
        }

        /// <summary>Leaves the callback invocation. Repeated disposal is harmless.</summary>
        public void Dispose()
        {
            NativeCallbackRegistration<TDelegate>? current = Interlocked.Exchange(ref owner, null);
            current?.ExitInvocation();
        }
    }
}
