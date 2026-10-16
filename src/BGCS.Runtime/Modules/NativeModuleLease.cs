using System;
using System.Threading;

namespace BGCS.Runtime;

/// <summary>Retains a module until a native object's final cleanup has completed.</summary>
public sealed class NativeModuleLease : IDisposable
{
    private NativeModuleSession? m_session;

    internal NativeModuleLease(NativeModuleSession session)
    {
        m_session = session;
    }

    internal bool isReleased => Volatile.Read(ref m_session) is null;
    internal bool IsActiveFor(NativeModuleSession session) => ReferenceEquals(Volatile.Read(ref m_session), session);

    // A logical reference count alone cannot prevent a custom provider's SafeHandle finalizer.
    // Retain the session/provider without retaining the failed resource or retrying its release.
    internal void RetainForUnresolvedCleanup(Exception failure)
        => Volatile.Read(ref m_session)?.RetainForUnresolvedCleanup(failure);

    /// <summary>Retains the actual provider after an ambiguous native cleanup failure and faults unload without retrying the release.</summary>
    /// <param name="call">An active call belonging to the same module, retained through native cleanup and this report.</param>
    /// <param name="failure">The original unresolved native cleanup failure.</param>
    /// <exception cref="ArgumentNullException">The call or failure is null.</exception>
    /// <exception cref="ObjectDisposedException">This resource lease has already been released.</exception>
    /// <exception cref="InvalidOperationException">The supplied call is released or belongs to another module.</exception>
    /// <remarks>The live resource and same-module call are validated atomically against unload. This method does not restore an unloaded module or establish cleanup thread safety.</remarks>
    public void RecordUnresolvedCleanup(
        NativeCallLease call,
        Exception failure
    ) {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(failure);
        NativeModuleSession? session = Volatile.Read(ref m_session);
        ObjectDisposedException.ThrowIf(session is null, this);
        session.RecordUnresolvedCleanup(this, call, failure);
    }

    /// <summary>Acquires a cleanup call while the resource retains its module, even after context closure.</summary>
    /// <returns>A lease that protects one native cleanup call.</returns>
    /// <exception cref="ObjectDisposedException">The resource lease has been released.</exception>
    public NativeCallLease AcquireCleanupCall()
    {
        NativeModuleSession? session = Volatile.Read(ref m_session);
        ObjectDisposedException.ThrowIf(session is null, this);
        return session.AcquireCleanupCall(this);
    }

    /// <summary>Starts a resource operation only while its public context remains active.</summary>
    /// <returns>A call lease protecting the operation.</returns>
    /// <exception cref="ObjectDisposedException">The resource or its context has closed.</exception>
    public NativeCallLease AcquireCall()
    {
        NativeModuleSession? session = Volatile.Read(ref m_session);
        ObjectDisposedException.ThrowIf(session is null, this);
        return session.AcquireResourceCall(this);
    }

    /// <summary>Releases resource ownership of the module exactly once.</summary>
    public void Dispose()
    {
        Interlocked.Exchange(ref m_session, null)?.ReleaseResourceLease();
    }
}
