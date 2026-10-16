using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace BGCS.Runtime;

/// <summary>Owns one native resource and retains its release module until cleanup completes.</summary>
public sealed class OwnedNativeHandle : SafeHandle
{
    private readonly NativeModuleLease m_module;
    private readonly Action<nint> m_release;
    private Exception? m_releaseFailure;

    /// <summary>Takes ownership of a native address and a previously acquired module lease.</summary>
    /// <param name="address">The owned native address; zero represents an invalid resource.</param>
    /// <param name="module">A resource lease acquired before creating the native object.</param>
    /// <param name="release">The matching release operation from the resource's allocator domain.</param>
    /// <param name="releaseDomain">The declared release-domain identity.</param>
    /// <exception cref="ArgumentNullException">The lease or release operation is null.</exception>
    /// <exception cref="ArgumentException">The release-domain identity is empty.</exception>
    public OwnedNativeHandle(
        nint address,
        NativeModuleLease module,
        Action<nint> release,
        string releaseDomain
    ) : base(0, ownsHandle: true)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(release);
        ArgumentException.ThrowIfNullOrWhiteSpace(releaseDomain);
        m_module = module;
        m_release = release;
        this.releaseDomain = releaseDomain;
        SetHandle(address);
        if (address == 0)
            module.Dispose();
    }

    /// <inheritdoc/>
    public override bool IsInvalid => handle == 0;
    /// <summary>Gets the explicit domain of the matching native destructor or deallocator.</summary>
    public string releaseDomain { get; }
    /// <summary>Gets a captured cleanup failure; a failed release retains its module instead of unloading it.</summary>
    public Exception? releaseFailure => Volatile.Read(ref m_releaseFailure);

    /// <summary>Invokes an operation while protecting both the resource and its module from disposal.</summary>
    /// <typeparam name="TResult">The managed operation result.</typeparam>
    /// <param name="operation">The native operation using the borrowed address.</param>
    /// <returns>The operation result.</returns>
    /// <exception cref="ObjectDisposedException">The handle or context has started closing.</exception>
    public TResult Invoke<TResult>(Func<nint, TResult> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        bool retained = false;
        try
        {
            DangerousAddRef(ref retained);
            ObjectDisposedException.ThrowIf(IsInvalid, this);
            using NativeCallLease call = m_module.AcquireCall();
            return operation(handle);
        }
        finally
        {
            if (retained)
                DangerousRelease();
        }
    }

    /// <summary>Creates a view that follows this owner's validity and never acquires ownership.</summary>
    /// <returns>A borrowed resource view.</returns>
    public BorrowedNativeHandle AsBorrowed() => new(this);

    /// <inheritdoc/>
    protected override bool ReleaseHandle()
    {
        try
        {
            using (NativeCallLease cleanup = m_module.AcquireCleanupCall())
                m_release(handle);
            m_module.Dispose();
            return true;
        }
        catch (Exception exception)
        {
            // Cleanup failure must not escape a finalizer or allow a still-live object to lose its module.
            Volatile.Write(ref m_releaseFailure, exception);
            m_module.RetainForUnresolvedCleanup(exception);
            return false;
        }
    }
}
