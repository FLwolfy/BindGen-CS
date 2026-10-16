using System;

namespace BGCS.Runtime;

/// <summary>Provides access to an owned native object without acquiring its destruction responsibility.</summary>
public readonly struct BorrowedNativeHandle
{
    private readonly OwnedNativeHandle m_owner;

    internal BorrowedNativeHandle(OwnedNativeHandle owner)
    {
        m_owner = owner;
    }

    /// <summary>Invokes through the owner's validity and concurrent-disposal protection.</summary>
    /// <typeparam name="TResult">The managed operation result.</typeparam>
    /// <param name="operation">The operation using a borrowed native address.</param>
    /// <returns>The operation result.</returns>
    /// <exception cref="ObjectDisposedException">The owner or its context has closed.</exception>
    public TResult Invoke<TResult>(Func<nint, TResult> operation)
    {
        ObjectDisposedException.ThrowIf(m_owner is null, this);
        return m_owner.Invoke(operation);
    }
}
