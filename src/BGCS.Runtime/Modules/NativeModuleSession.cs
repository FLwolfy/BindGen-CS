using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace BGCS.Runtime;

/// <summary>Prevents module unload while calls, resources or callback registrations hold leases.</summary>
public sealed class NativeModuleSession : IDisposable, IAsyncDisposable
{
    private readonly object m_gate = new();
    private readonly INativeModuleProvider m_provider;
    private readonly TaskCompletionSource m_unloaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int m_references = 1;
    private int m_resourceReferences;
    private bool m_closing;
    private bool m_unloading;
    private Exception? m_unloadFailure;
    private bool m_unresolvedCleanup;
    private GCHandle m_cleanupRetention;
    private readonly bool m_initialized;

    /// <summary>Takes ownership of an independent native module provider.</summary>
    /// <param name="provider">The provider released after the final lease. A provider that retains this session requires explicit disposal.</param>
    /// <exception cref="ArgumentNullException">The provider is null.</exception>
    public NativeModuleSession(INativeModuleProvider provider)
    {
        m_provider = provider ?? throw new ArgumentNullException(nameof(provider));
        // Keep the provider out of the finalization queue before the session's finalizer can close
        // it. Ordinary open sessions remain collectible unless their provider captures the session.
        // Resource acquisition upgrades this preallocated root to the session without allocating.
        m_cleanupRetention = GCHandle.Alloc(provider, GCHandleType.Normal);
        m_initialized = true;
    }

    /// <summary>Closes an abandoned context while retaining cleanup access for outstanding resource leases.</summary>
    ~NativeModuleSession()
    {
        if (m_initialized) Dispose();
    }

    /// <summary>Gets whether new public calls have been stopped.</summary>
    public bool isClosing
    {
        get
        {
            lock (m_gate)
                return m_closing;
        }
    }

    /// <summary>Gets a captured unload failure, or null before a failure occurs.</summary>
    public Exception? unloadFailure
    {
        get
        {
            lock (m_gate)
                return m_unloadFailure;
        }
    }

    /// <summary>Acquires a lease for a new public native call.</summary>
    /// <returns>A call lease that must be disposed after the native call returns.</returns>
    /// <exception cref="ObjectDisposedException">The context has started closing.</exception>
    public NativeCallLease AcquireCall()
    {
        lock (m_gate)
        {
            ObjectDisposedException.ThrowIf(m_closing, this);
            int references = checked(m_references + 1);
            NativeCallLease lease = new(this);
            m_references = references;
            return lease;
        }
    }

    /// <summary>Retains the module for a newly created native resource or registration.</summary>
    /// <returns>A resource lease that outlives the creating call.</returns>
    /// <exception cref="ObjectDisposedException">The context has started closing.</exception>
    public NativeModuleLease AcquireResource()
    {
        lock (m_gate)
        {
            ObjectDisposedException.ThrowIf(m_closing, this);
            int references = checked(m_references + 1);
            int resources = checked(m_resourceReferences + 1);
            NativeModuleLease lease = new(this);
            if (m_resourceReferences == 0) RetainCleanupNoLock();
            m_references = references;
            m_resourceReferences = resources;
            return lease;
        }
    }

    /// <summary>Resolves every required symbol before exposing an immutable export table.</summary>
    /// <param name="names">The complete required native export names.</param>
    /// <returns>A frozen table containing only nonzero addresses.</returns>
    /// <exception cref="EntryPointNotFoundException">Any required symbol is missing.</exception>
    /// <exception cref="ArgumentException">An export identity occurs more than once.</exception>
    public IReadOnlyDictionary<string, nint> ResolveRequiredSymbols(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        using NativeCallLease call = AcquireCall();
        Dictionary<string, nint> exports = new(StringComparer.Ordinal);
        foreach (string name in names)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            if (!m_provider.TryGetExport(name, out nint address) || address == 0)
                throw new EntryPointNotFoundException($"Required native symbol '{name}' is unavailable.");
            exports.Add(name, address);
        }
        return exports.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>Stops new calls while existing resources retain cleanup access to the module.</summary>
    public void Dispose()
    {
        bool unload;
        lock (m_gate)
        {
            if (m_closing)
                return;
            m_closing = true;
            unload = --m_references == 0 && !m_unresolvedCleanup;
            RetainCleanupNoLock();
            if (unload)
                m_unloading = true;
        }
        if (unload)
            Unload();
        GC.SuppressFinalize(this);
    }

    /// <summary>Stops new calls and waits until all calls and resources release the module.</summary>
    /// <returns>The final unload operation, faulted promptly when terminal resource or provider cleanup failed.</returns>
    /// <remarks>Dispose owned resources before awaiting this method to avoid waiting on retained leases.</remarks>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return new(m_unloaded.Task);
    }

    internal NativeCallLease AcquireCleanupCall(NativeModuleLease owner)
    {
        lock (m_gate)
        {
            ObjectDisposedException.ThrowIf(owner.isReleased || m_unloading, owner);
            int references = checked(m_references + 1);
            NativeCallLease lease = new(this);
            m_references = references;
            return lease;
        }
    }

    internal NativeCallLease AcquireResourceCall(NativeModuleLease owner)
    {
        lock (m_gate)
        {
            ObjectDisposedException.ThrowIf(owner.isReleased || m_closing, owner);
            int references = checked(m_references + 1);
            NativeCallLease lease = new(this);
            m_references = references;
            return lease;
        }
    }

    internal void ReleaseLease() => ReleaseLease(resource: false);

    internal void ReleaseResourceLease() => ReleaseLease(resource: true);

    private void ReleaseLease(bool resource)
    {
        bool unload;
        lock (m_gate)
        {
            if (resource) --m_resourceReferences;
            unload = --m_references == 0 && m_closing && !m_unresolvedCleanup;
            if (unload)
                m_unloading = true;
            else if (resource && m_resourceReferences == 0 && !m_closing && !m_unresolvedCleanup)
                m_cleanupRetention.Target = m_provider;
        }
        if (unload)
            Unload();
    }

    internal void RetainForUnresolvedCleanup(Exception failure)
    {
        lock (m_gate)
        {
            m_unresolvedCleanup = true;
            RetainCleanupNoLock();
        }
        m_unloaded.TrySetException(failure);
    }

    internal void RecordUnresolvedCleanup(
        NativeModuleLease owner,
        NativeCallLease call,
        Exception failure
    ) {
        lock (m_gate)
        {
            ObjectDisposedException.ThrowIf(!owner.IsActiveFor(this) || m_unloading, owner);
            if (!call.IsActiveFor(this)) throw new InvalidOperationException("An unresolved native cleanup must be recorded under a live call belonging to the same module.");
            m_unresolvedCleanup = true;
            RetainCleanupNoLock();
        }
        m_unloaded.TrySetException(failure);
    }

    private void RetainCleanupNoLock()
    {
        if (m_cleanupRetention.IsAllocated) m_cleanupRetention.Target = this;
    }

    private void Unload()
    {
        try
        {
            m_provider.Dispose();
            lock (m_gate)
                if (m_cleanupRetention.IsAllocated) m_cleanupRetention.Free();
            m_unloaded.TrySetResult();
        }
        catch (Exception exception)
        {
            lock (m_gate)
            {
                m_unloadFailure = exception;
                RetainCleanupNoLock();
            }
            m_unloaded.TrySetException(exception);
        }
    }
}
