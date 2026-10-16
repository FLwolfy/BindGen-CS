using System;
using System.Collections.Generic;

namespace BGCS.Runtime;

/// <summary>Captures callback failures in one bounded instance without throwing through reverse native calls.</summary>
public sealed class CallbackExceptionBoundary
{
    private readonly object m_gate = new();
    private readonly Queue<Exception> m_errors = [];
    private readonly int m_capacity;
    private long m_dropped;

    /// <summary>Creates an independent error boundary for a library context or registration.</summary>
    /// <param name="capacity">Maximum retained failures; older failures are discarded when full.</param>
    /// <exception cref="ArgumentOutOfRangeException">Capacity is not positive.</exception>
    public CallbackExceptionBoundary(int capacity = 64)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        m_capacity = capacity;
    }

    /// <summary>Gets how many older failures were discarded because retention was full.</summary>
    public long droppedErrorCount
    {
        get { lock (m_gate) return m_dropped; }
    }

    /// <summary>Retrieves and removes the oldest retained callback failure.</summary>
    /// <returns>The next failure, or null when no failure is retained.</returns>
    public Exception? TakeException()
    {
        lock (m_gate)
            return m_errors.TryDequeue(out Exception? error) ? error : null;
    }

    /// <summary>Invokes a callback while capturing managed failures in this instance.</summary>
    /// <param name="callback">The managed callback body.</param>
    /// <returns>True when the callback completed without an exception.</returns>
    public bool Invoke(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        try { callback(); return true; }
        catch (Exception exception) { Capture(exception); return false; }
    }

    /// <summary>Invokes a callback and returns an explicit fallback if managed code throws.</summary>
    /// <typeparam name="T">The callback return type.</typeparam>
    /// <param name="callback">The managed callback body.</param>
    /// <param name="fallback">The native-compatible fallback value.</param>
    /// <returns>The callback result or fallback after a captured exception.</returns>
    public T Invoke<T>(
        Func<T> callback,
        T fallback = default!
    ) {
        ArgumentNullException.ThrowIfNull(callback);
        try { return callback(); }
        catch (Exception exception) { Capture(exception); return fallback; }
    }

    /// <summary>Records one caught callback failure without rethrowing it across the native boundary.</summary>
    /// <param name="exception">The managed failure captured by a generated static thunk.</param>
    public void Capture(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        lock (m_gate)
        {
            if (m_errors.Count == m_capacity)
            {
                m_errors.Dequeue();
                m_dropped++;
            }
            m_errors.Enqueue(exception);
        }
    }
}
