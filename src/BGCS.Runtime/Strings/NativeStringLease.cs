using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace BGCS.Runtime;

/// <summary>Owns temporary managed-created native text until one declared nonretaining call ends.</summary>
public sealed unsafe class NativeStringLease : IDisposable, IAsyncDisposable
{
    private nint m_pointer;
    private readonly bool m_aligned;
    private readonly NativeBuffer? m_buffer;

    internal NativeStringLease(
        nint pointer,
        bool aligned = false,
        int encodedCodeUnitCount = 0
    ) {
        m_pointer = pointer;
        m_aligned = aligned;
        this.encodedCodeUnitCount = encodedCodeUnitCount;
    }

    private NativeStringLease(
        NativeBuffer buffer,
        int encodedCodeUnitCount
    ) {
        m_buffer = buffer;
        m_pointer = buffer.DangerousGetHandle();
        this.encodedCodeUnitCount = encodedCodeUnitCount;
    }

    internal static NativeStringLease Allocate(
        nuint byteLength,
        nuint requiredAlignment,
        NativeAllocator? allocator = null,
        int encodedCodeUnitCount = 0
    ) {
        if (requiredAlignment == 0 || (requiredAlignment & (requiredAlignment - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(requiredAlignment), "Native text alignment must be a positive power of two.");
        if (allocator is not null) return new(allocator.Allocate(byteLength, requiredAlignment), encodedCodeUnitCount);
        nuint alignment = nuint.Max(requiredAlignment, (nuint)IntPtr.Size);
        nuint length = checked(nuint.Max(byteLength, 1) + alignment - 1) & ~(alignment - 1);
        nint pointer = (nint)NativeMemory.AlignedAlloc(length, alignment);
        if (pointer == 0) throw new OutOfMemoryException("The aligned native text allocation failed.");
        return new(pointer, aligned: true, encodedCodeUnitCount: encodedCodeUnitCount);
    }

    /// <summary>Releases abandoned text through its original pair without allowing cleanup failure to escape finalization.</summary>
    ~NativeStringLease() => Release(throwOnFailure: false);

    /// <summary>Gets the borrowed text address; its validity ends when this lease is disposed.</summary>
    public nint pointer => Volatile.Read(ref m_pointer);

    /// <summary>Gets the exact encoded code-unit count excluding the trailing terminator; null input has count zero.</summary>
    public int encodedCodeUnitCount { get; }

    /// <summary>Gets an ambiguous custom cleanup failure, or null before a failure or for framework-created storage.</summary>
    public Exception? releaseFailure => m_buffer?.releaseFailure;

    /// <summary>Stops access and releases through the original allocation pair exactly once, queueing affine cleanup when required.</summary>
    /// <exception cref="InvalidOperationException">A custom free has already failed; the release module remains retained and the free is not retried.</exception>
    public void Dispose()
    {
        try { Release(throwOnFailure: true); }
        finally { GC.SuppressFinalize(this); }
    }

    /// <summary>Closes temporary text and awaits actual queued custom cleanup instead of assuming that posting completed the free.</summary>
    /// <returns>The original buffer's cleanup operation, faulted on ambiguous free failure.</returns>
    public ValueTask DisposeAsync()
    {
        Release(throwOnFailure: false);
        GC.SuppressFinalize(this);
        return m_buffer?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    private void Release(bool throwOnFailure)
    {
        nint pointer = Interlocked.Exchange(ref m_pointer, 0);
        if (pointer != 0)
        {
            if (m_buffer is not null) m_buffer.Dispose();
            else if (m_aligned) NativeMemory.AlignedFree((void*)pointer);
            else NativeMemory.Free((void*)pointer);
        }
        if (throwOnFailure && releaseFailure is Exception failure)
            throw new InvalidOperationException("The custom native text release failed; its module was retained without retrying an ambiguous free.", failure);
    }
}
