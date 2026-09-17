using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BGCS.Runtime;

/// <summary>
/// Owns multiple native callback leases keyed by a stable managed registration identifier.
/// </summary>
/// <typeparam name="TKey">Registration key type.</typeparam>
/// <typeparam name="TDelegate">Managed callback delegate type.</typeparam>
public sealed class NativeCallbackRegistry<TKey, TDelegate> : IDisposable
    where TKey : notnull
    where TDelegate : Delegate
{
    private readonly object m_sync = new();
    private readonly Dictionary<TKey, NativeCallback<TDelegate>> m_callbacks = [];
    private readonly List<NativeCallback<TDelegate>> m_retired = [];
    private bool m_disposed;

    /// <summary>
    /// Gets the number of active callback leases.
    /// </summary>
    public int Count
    {
        get
        {
            lock (m_sync)
                return m_callbacks.Count;
        }
    }

    /// <summary>
    /// Registers or replaces a callback and returns its unmanaged function pointer.
    /// </summary>
    /// <param name="key">Stable registration key.</param>
    /// <param name="callback">Callback retained until replacement, removal, or registry disposal.</param>
    /// <returns>The unmanaged callback pointer.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="callback"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">Thrown after the registry has been disposed.</exception>
    public nint Register(TKey key, TDelegate callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        NativeCallback<TDelegate> replacement = new(callback);
        lock (m_sync)
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
            if (m_callbacks.Remove(key, out NativeCallback<TDelegate> previous))
                m_retired.Add(previous);
            m_callbacks.Add(key, replacement);
            return Marshal.GetFunctionPointerForDelegate(callback);
        }
    }

    /// <summary>
    /// Removes a callback lease if the key is registered.
    /// </summary>
    /// <param name="key">Registration key to remove.</param>
    /// <returns><see langword="true"/> when a callback was removed.</returns>
    public bool Unregister(TKey key)
    {
        lock (m_sync)
        {
            if (!m_callbacks.Remove(key, out NativeCallback<TDelegate> callback))
                return false;
            m_retired.Add(callback);
            return true;
        }
    }

    /// <summary>
    /// Releases callbacks retired by replacement or unregistration after native code has stopped invoking their pointers.
    /// </summary>
    /// <remarks>Call this only after the native unregister operation has completed and synchronized with in-flight callbacks.</remarks>
    public void ReleaseRetired()
    {
        lock (m_sync)
        {
            foreach (NativeCallback<TDelegate> callback in m_retired)
                callback.Dispose();
            m_retired.Clear();
        }
    }

    /// <summary>
    /// Attempts to retrieve a currently registered managed callback.
    /// </summary>
    /// <param name="key">Registration key.</param>
    /// <param name="callback">Receives the callback when registered.</param>
    /// <returns><see langword="true"/> when the key is registered.</returns>
    public bool TryGet(TKey key, out TDelegate? callback)
    {
        lock (m_sync)
        {
            if (m_callbacks.TryGetValue(key, out NativeCallback<TDelegate> lease))
            {
                callback = lease.Callback;
                return callback != null;
            }
            callback = null;
            return false;
        }
    }

    /// <summary>
    /// Releases all callback leases. Repeated calls are safe.
    /// </summary>
    public void Dispose()
    {
        lock (m_sync)
        {
            if (m_disposed)
                return;
            foreach (NativeCallback<TDelegate> callback in m_callbacks.Values)
                callback.Dispose();
            foreach (NativeCallback<TDelegate> callback in m_retired)
                callback.Dispose();
            m_callbacks.Clear();
            m_retired.Clear();
            m_disposed = true;
        }
    }
}
