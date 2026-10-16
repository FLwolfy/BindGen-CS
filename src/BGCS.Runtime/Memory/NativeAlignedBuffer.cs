using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace BGCS.Runtime;

/// <summary>Owns native storage with an explicit alignment that ordinary managed allocations cannot promise.</summary>
public sealed unsafe class NativeAlignedBuffer : IDisposable
{
    private nint m_address;

    /// <summary>Allocates aligned native storage using the matching NativeMemory allocator pair.</summary>
    /// <param name="byteLength">The requested readable and writable extent.</param>
    /// <param name="alignment">A power-of-two alignment at least as large as the host pointer.</param>
    /// <exception cref="ArgumentOutOfRangeException">The alignment is invalid.</exception>
    /// <exception cref="OutOfMemoryException">The aligned allocation cannot be satisfied.</exception>
    public NativeAlignedBuffer(
        nuint byteLength,
        nuint alignment
    ) {
        if (alignment < (nuint)IntPtr.Size || (alignment & (alignment - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(alignment), "Alignment must be a power of two at least as large as the host pointer.");
        nuint nonemptyLength = byteLength == 0 ? 1 : byteLength;
        nuint allocationLength = checked(nonemptyLength + alignment - 1) & ~(alignment - 1);
        m_address = (nint)NativeMemory.AlignedAlloc(allocationLength, alignment);
        if (m_address == 0)
            throw new OutOfMemoryException("The aligned native allocation failed.");
        this.byteLength = byteLength;
        this.alignment = alignment;
    }

    /// <summary>Gets the requested allocation extent in bytes.</summary>
    public nuint byteLength { get; }
    /// <summary>Gets the guaranteed address alignment in bytes.</summary>
    public nuint alignment { get; }
    /// <summary>Gets a borrowed address valid until disposal; callers must synchronize use against disposal.</summary>
    /// <exception cref="ObjectDisposedException">The allocation was released.</exception>
    public nint address
    {
        get
        {
            nint pointer = Volatile.Read(ref m_address);
            ObjectDisposedException.ThrowIf(pointer == 0, this);
            return pointer;
        }
    }

    /// <summary>Releases native storage with its matching aligned deallocator.</summary>
    public void Dispose()
    {
        nint pointer = Interlocked.Exchange(ref m_address, 0);
        if (pointer != 0)
            NativeMemory.AlignedFree((void*)pointer);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases an allocation when its owner did not dispose it explicitly.</summary>
    ~NativeAlignedBuffer()
    {
        Dispose();
    }
}
