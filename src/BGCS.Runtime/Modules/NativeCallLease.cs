using System;
using System.Threading;

namespace BGCS.Runtime;

/// <summary>Keeps a module loaded until one already-started native call returns.</summary>
public sealed class NativeCallLease : IDisposable
{
    private NativeModuleSession? m_session;

    internal NativeCallLease(NativeModuleSession session)
    {
        m_session = session;
    }

    internal bool IsActiveFor(NativeModuleSession session) => ReferenceEquals(Volatile.Read(ref m_session), session);

    /// <summary>Ends the protected native call exactly once.</summary>
    public void Dispose()
    {
        Interlocked.Exchange(ref m_session, null)?.ReleaseLease();
    }
}
