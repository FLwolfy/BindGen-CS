using System;
using System.Text;

namespace BGCS.Runtime;

internal static class NativeTextEncoder
{
    internal static unsafe NativeStringLease Encode(
        string? value,
        bool allowsNull,
        Encoding encoding,
        int width,
        bool zeroTerminated,
        int maximumCodeUnits,
        nuint requiredAlignment,
        NativeAllocator? allocator
    ) {
        if (maximumCodeUnits <= 0) throw new ArgumentOutOfRangeException(nameof(maximumCodeUnits));
        if (value is null)
        {
            if (!allowsNull) throw new ArgumentNullException(nameof(value));
            return new(0);
        }
        if (zeroTerminated && value.AsSpan().Contains('\0'))
            throw new ArgumentException("A zero-terminated native input cannot contain embedded null characters.", nameof(value));
        int bytes = encoding.GetByteCount(value);
        int units = checked(bytes / width);
        if (units > maximumCodeUnits) throw new ArgumentOutOfRangeException(nameof(value), "Encoded text exceeds the declared code-unit bound.");
        // Counted empty strings retain a non-null address. Extra termination is outside the declared count.
        NativeStringLease lease = NativeStringLease.Allocate(checked((nuint)bytes + (nuint)width), requiredAlignment, allocator, units);
        try
        {
            Span<byte> storage = new((void*)lease.pointer, checked(bytes + width));
            encoding.GetBytes(value.AsSpan(), storage[..bytes]);
            storage[bytes..].Clear();
            return lease;
        }
        catch (Exception failure)
        {
            try { lease.Dispose(); }
            catch (Exception cleanupFailure) { throw new AggregateException("Native text encoding and matching cleanup both failed.", failure, cleanupFailure); }
            throw;
        }
    }
}
