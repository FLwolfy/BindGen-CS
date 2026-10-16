using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace BGCS.Runtime;

/// <summary>Owns allocator-bound storage and keeps in-flight operations and the releasing module alive.</summary>
public sealed class NativeBuffer : SafeHandle, IAsyncDisposable
{
    private readonly NativeAllocator m_allocator;
    private readonly NativeModuleLease? m_module;
    private readonly TaskCompletionSource m_released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int m_releaseStarted;
    private Exception? m_releaseFailure;

    internal NativeBuffer(
        NativeAllocator allocator,
        nint address,
        nuint byteLength,
        nuint allocatedByteLength,
        nuint alignment,
        NativeModuleLease? module
    ) : base(0, ownsHandle: true)
    {
        m_allocator = allocator;
        m_module = module;
        this.byteLength = byteLength;
        this.allocatedByteLength = allocatedByteLength;
        this.alignment = alignment;
        SetHandle(address);
    }

    /// <inheritdoc/>
    public override bool IsInvalid => handle == 0;
    /// <summary>Gets the concrete allocator identity without permitting release through a different pair.</summary>
    public string allocatorId => m_allocator.allocatorId;
    /// <summary>Gets the requested logical extent; a zero-length buffer still owns a nonzero physical allocation.</summary>
    public nuint byteLength { get; }
    /// <summary>Gets the checked rounded extent passed unchanged to both native callbacks.</summary>
    public nuint allocatedByteLength { get; }
    /// <summary>Gets the requested and validated address alignment.</summary>
    public nuint alignment { get; }
    /// <summary>Gets a captured terminal cleanup failure; ambiguous failures are never retried and retain their module.</summary>
    public Exception? releaseFailure => Volatile.Read(ref m_releaseFailure);

    /// <summary>Executes an operation while retaining the buffer and its module through concurrent disposal.</summary>
    /// <typeparam name="TResult">The operation's managed result.</typeparam>
    /// <param name="operation">Uses the borrowed address and logical extent only during this callback.</param>
    /// <returns>The operation result.</returns>
    /// <exception cref="ObjectDisposedException">The allocation or its module has started closing.</exception>
    /// <exception cref="InvalidOperationException">The current thread violates the allocator's thread contract.</exception>
    public TResult Invoke<TResult>(Func<nint, nuint, TResult> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        m_allocator.CheckAccess();
        bool retained = false;
        try
        {
            DangerousAddRef(ref retained);
            ObjectDisposedException.ThrowIf(IsInvalid, this);
            using NativeCallLease? call = m_module?.AcquireCall();
            return operation(handle, byteLength);
        }
        finally { if (retained) DangerousRelease(); }
    }

    /// <summary>Closes the buffer and waits for the actual matching free, including deferred affine cleanup.</summary>
    /// <returns>The cleanup operation, faulted when release failed ambiguously and its module was retained.</returns>
    /// <remarks>Dispose stops new access immediately; it does not block for an active call or dispatcher. Keep a dispatcher pumping while awaiting this operation.</remarks>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return new(m_released.Task);
    }

    /// <inheritdoc/>
    protected override bool ReleaseHandle()
    {
        try { m_allocator.ScheduleRelease(Cleanup); }
        catch (Exception failure) { Failed(failure); }
        return releaseFailure is null;
    }

    private void Cleanup()
    {
        if (Interlocked.Exchange(ref m_releaseStarted, 1) != 0) return;
        try
        {
            using (NativeCallLease? cleanup = m_module?.AcquireCleanupCall())
                m_allocator.Release(handle, allocatedByteLength, alignment);
            m_module?.Dispose();
            m_released.TrySetResult();
        }
        catch (Exception failure) { Failed(failure); }
    }

    private void Failed(Exception failure)
    {
        if (m_released.Task.IsCompletedSuccessfully) return;
        Interlocked.CompareExchange(ref m_releaseFailure, failure, null);
        m_module?.RetainForUnresolvedCleanup(failure);
        m_released.TrySetException(failure);
    }
}
