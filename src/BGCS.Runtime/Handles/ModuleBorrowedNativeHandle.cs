using System;
using System.Runtime.InteropServices;

namespace BGCS.Runtime;

/// <summary>Retains a module for a borrowed native address without acquiring native destruction responsibility.</summary>
public sealed class ModuleBorrowedNativeHandle : SafeHandle
{
    private readonly NativeModuleLease m_module;

    /// <summary>Adopts a previously acquired module lease for an explicitly module-lifetime borrowed address.</summary>
    /// <param name="address">The borrowed native address; zero creates an invalid reference and immediately releases the supplied lease.</param>
    /// <param name="module">The independent resource lease transferred to this reference after nonnull validation.</param>
    /// <remarks>The native protocol must guarantee address validity until module unload. Retaining code alone does not extend an arbitrary native object's lifetime. Do not independently dispose a transferred lease.</remarks>
    /// <exception cref="ArgumentNullException">The supplied lease is null; no lease is adopted.</exception>
    public ModuleBorrowedNativeHandle(
        nint address,
        NativeModuleLease module
    ) : base(0, ownsHandle: true)
    {
        ArgumentNullException.ThrowIfNull(module);
        m_module = module;
        SetHandle(address);
        if (address == 0) module.Dispose();
    }

    /// <inheritdoc/>
    public override bool IsInvalid => handle == 0;

    /// <summary>Invokes an operation while protecting this reference and its module from concurrent disposal.</summary>
    /// <typeparam name="TResult">The managed operation result.</typeparam>
    /// <param name="operation">The synchronous operation borrowing the native address for its duration.</param>
    /// <returns>The operation result.</returns>
    /// <exception cref="ArgumentNullException">The operation is null.</exception>
    /// <exception cref="ObjectDisposedException">The reference is invalid or disposed, or its public context has closed.</exception>
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
            if (retained) DangerousRelease();
        }
    }

    /// <inheritdoc/>
    protected override bool ReleaseHandle()
    {
        // This releases only the module reference. NativeModuleSession captures
        // provider-unload failures, including when this runs on the finalizer.
        m_module.Dispose();
        return true;
    }
}
