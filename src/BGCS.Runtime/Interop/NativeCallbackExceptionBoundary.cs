using System;

namespace BGCS.Runtime;

/// <summary>
/// Prevents managed exceptions from crossing reverse P/Invoke callback boundaries.
/// </summary>
public static class NativeCallbackExceptionBoundary
{
    [ThreadStatic]
    private static Exception? m_lastException;

    /// <summary>Gets and clears the last callback exception captured on the current thread.</summary>
    /// <returns>The captured exception, or <see langword="null"/>.</returns>
    public static Exception? TakeLastException()
    {
        Exception? exception = m_lastException;
        m_lastException = null;
        return exception;
    }

    /// <summary>Invokes a callback and captures exceptions instead of allowing them to cross native code.</summary>
    /// <param name="callback">Managed callback body.</param>
    /// <returns><see langword="true"/> on success; otherwise <see langword="false"/>.</returns>
    public static bool Invoke(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        try
        {
            callback();
            return true;
        }
        catch (Exception exception)
        {
            m_lastException = exception;
            return false;
        }
    }

    /// <summary>Invokes a callback and returns a fallback value when managed code throws.</summary>
    /// <typeparam name="T">Callback result type.</typeparam>
    /// <param name="callback">Managed callback body.</param>
    /// <param name="fallback">Result returned after an exception.</param>
    /// <returns>The callback result or <paramref name="fallback"/>.</returns>
    public static T Invoke<T>(Func<T> callback, T fallback = default!)
    {
        ArgumentNullException.ThrowIfNull(callback);
        try
        {
            return callback();
        }
        catch (Exception exception)
        {
            m_lastException = exception;
            return fallback;
        }
    }
}
