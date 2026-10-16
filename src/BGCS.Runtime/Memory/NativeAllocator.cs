using System;
using System.Runtime.InteropServices;

namespace BGCS.Runtime;

/// <summary>Keeps one immutable native allocation/release pair, thread contract and optional module lifetime together.</summary>
public sealed class NativeAllocator
{
    private readonly Func<nuint, nuint, nint> m_allocate;
    private readonly Action<nint, nuint, nuint> m_release;
    private readonly NativeModuleSession? m_module;
    private readonly INativeReleaseDispatcher? m_dispatcher;

    /// <summary>Declares the actual callbacks of one allocator instead of inferring an implementation from a domain name.</summary>
    /// <param name="allocatorId">The diagnostic identity of this concrete allocator pair.</param>
    /// <param name="allocate">Allocates the supplied physical extent and alignment; a throwing callback must not transfer ownership.</param>
    /// <param name="release">Releases the same address, physical extent and alignment through the matching allocator.</param>
    /// <param name="threadDomain">The caller's explicit native allocation and cleanup thread assertion.</param>
    /// <param name="module">An optional module whose resource lease protects every allocation through native cleanup.</param>
    /// <param name="dispatcher">The required dispatcher for a Dispatcher contract, otherwise null.</param>
    /// <exception cref="ArgumentException">An identity or thread contract is invalid or the dispatcher does not match the contract.</exception>
    /// <exception cref="ArgumentNullException">An allocation or release callback is null.</exception>
    /// <remarks>Callback state must remain valid for the lifetime of every returned buffer. Bind native state cleanup to the supplied module provider when it shares that lifetime. An ambiguous release exception is terminal: it is captured without retrying or unloading its module.</remarks>
    public NativeAllocator(
        string allocatorId,
        Func<nuint, nuint, nint> allocate,
        Action<nint, nuint, nuint> release,
        NativeAllocatorThreadDomain threadDomain,
        NativeModuleSession? module = null,
        INativeReleaseDispatcher? dispatcher = null
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(allocatorId);
        ArgumentNullException.ThrowIfNull(allocate);
        ArgumentNullException.ThrowIfNull(release);
        if (threadDomain is not (NativeAllocatorThreadDomain.AnyThread or NativeAllocatorThreadDomain.Dispatcher))
            throw new ArgumentException("Native allocation requires an explicit supported allocation and cleanup thread contract.", nameof(threadDomain));
        if ((threadDomain == NativeAllocatorThreadDomain.Dispatcher) != (dispatcher is not null))
            throw new ArgumentException("A dispatcher is required exactly for the Dispatcher allocation thread contract.", nameof(dispatcher));
        this.allocatorId = allocatorId;
        this.threadDomain = threadDomain;
        m_allocate = allocate;
        m_release = release;
        m_module = module;
        m_dispatcher = dispatcher;
    }

    /// <summary>Gets the identity of the concrete allocation and release pair retained by each buffer.</summary>
    public string allocatorId { get; }
    /// <summary>Gets the caller-declared native callback thread domain, without claiming independent verification.</summary>
    public NativeAllocatorThreadDomain threadDomain { get; }

    /// <summary>Checks that an injected allocator retains the exact context session required by its consumer.</summary>
    /// <param name="module">The session whose closure must not invalidate this allocator's matching native cleanup.</param>
    /// <returns>True only when this allocator was created with the identical session instance.</returns>
    public bool IsBoundTo(NativeModuleSession module)
    {
        ArgumentNullException.ThrowIfNull(module);
        return ReferenceEquals(m_module, module);
    }

    /// <summary>Allocates owned storage with a positive power-of-two alignment and a checked physical extent.</summary>
    /// <param name="byteLength">The logical accessible extent, which may be zero.</param>
    /// <param name="alignment">The required address alignment, including alignments smaller than a native pointer.</param>
    /// <returns>A buffer bound permanently to this matching release pair.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Alignment is zero or is not a power of two.</exception>
    /// <exception cref="OverflowException">Rounding the physical extent would overflow the native size type.</exception>
    /// <exception cref="OutOfMemoryException">The allocator returns a null address.</exception>
    /// <exception cref="InvalidOperationException">The current thread or returned address violates the allocator's contract.</exception>
    /// <exception cref="ObjectDisposedException">The supplied module is closing.</exception>
    public NativeBuffer Allocate(
        nuint byteLength,
        nuint alignment
    ) {
        if (alignment == 0 || (alignment & (alignment - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(alignment), "Native allocation alignment must be a positive power of two.");
        nuint allocatedByteLength = checked(nuint.Max(byteLength, 1) + alignment - 1) & ~(alignment - 1);
        CheckAccess();
        NativeModuleLease? resource = m_module?.AcquireResource();
        try
        {
            using NativeCallLease? call = resource?.AcquireCall();
            nint address = m_allocate(allocatedByteLength, alignment);
            if (address == 0) throw new OutOfMemoryException("The declared native allocator returned a null address.");
            NativeBuffer buffer = new(this, address, byteLength, allocatedByteLength, alignment, resource);
            resource = null;
            if ((nuint)address % alignment != 0)
            {
                buffer.Dispose();
                throw new InvalidOperationException("The native allocator returned an address that does not satisfy its requested alignment.", buffer.releaseFailure);
            }
            return buffer;
        }
        finally { resource?.Dispose(); }
    }

    /// <summary>Creates an independent allocator using the matching framework aligned allocation/free pair.</summary>
    /// <returns>A stateless native-memory allocator supporting any cleanup thread.</returns>
    public static unsafe NativeAllocator CreateNativeMemory() => new("System.Runtime.NativeMemory",
        (
            length,
            alignment
        ) => {
            nuint physicalAlignment = nuint.Max(alignment, (nuint)IntPtr.Size);
            nuint physicalLength = checked(length + physicalAlignment - 1) & ~(physicalAlignment - 1);
            return (nint)NativeMemory.AlignedAlloc(physicalLength, physicalAlignment);
        }, (
            address,
            _,
            _
        ) => NativeMemory.AlignedFree((void*)address), NativeAllocatorThreadDomain.AnyThread);

    internal void CheckAccess()
    {
        if (m_dispatcher is not null && !m_dispatcher.CheckAccess())
            throw new InvalidOperationException("The native allocator operation requires its declared dispatch thread.");
    }

    internal void ScheduleRelease(Action cleanup)
    {
        if (m_dispatcher is null || m_dispatcher.CheckAccess()) cleanup();
        else m_dispatcher.Post(cleanup);
    }

    internal void Release(
        nint address,
        nuint byteLength,
        nuint alignment
    ) {
        CheckAccess();
        m_release(address, byteLength, alignment);
    }
}
