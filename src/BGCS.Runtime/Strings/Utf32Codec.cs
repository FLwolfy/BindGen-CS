using System;
using System.Runtime.InteropServices;
using System.Text;

namespace BGCS.Runtime;

/// <summary>
/// Converts explicitly ordered UTF-32 native strings without interpreting them as UTF-16.
/// </summary>
public static unsafe class Utf32Codec
{
    private static readonly UTF32Encoding Encoding = new(false, false, true);
    private static readonly UTF32Encoding s_big = new(true, false, true);

    /// <summary>Allocates aligned UTF-32 code units for one explicitly nonretaining native call.</summary>
    /// <param name="value">The managed Unicode input.</param>
    /// <param name="allowsNull">Whether native code permits a null text address.</param>
    /// <param name="bigEndian">Explicit byte order, or null for the consuming process's native order.</param>
    /// <param name="requiredAlignment">The declared native pointee alignment.</param>
    /// <returns>The independently owned zero-terminated native text lease.</returns>
    /// <exception cref="ArgumentException">Embedded null code points would truncate the C string.</exception>
    /// <exception cref="ArgumentNullException">The contract forbids null.</exception>
    /// <exception cref="EncoderFallbackException">The managed input contains invalid Unicode.</exception>
    public static NativeStringLease EncodeNullTerminated(
        string? value,
        bool allowsNull,
        bool? bigEndian = null,
        nuint requiredAlignment = 4
    )
        => EncodeNullTerminated(value, allowsNull, bigEndian, requiredAlignment, allocator: null);

    /// <summary>Encodes nonretained UTF-32 input through an explicit allocator/free pair and code-unit byte order.</summary>
    /// <param name="value">The managed Unicode input.</param>
    /// <param name="allowsNull">The native null-pointer contract.</param>
    /// <param name="bigEndian">The code-unit byte order, or null for native order.</param>
    /// <param name="requiredAlignment">The declared native pointee alignment.</param>
    /// <param name="allocator">The custom allocation pair, or null for unchanged framework allocation.</param>
    /// <returns>Temporary text retaining the actual matching allocator and optional module.</returns>
    public static NativeStringLease EncodeNullTerminated(
        string? value,
        bool allowsNull,
        bool? bigEndian,
        nuint requiredAlignment,
        NativeAllocator? allocator
    )
        => NativeTextEncoder.Encode(value, allowsNull, (bigEndian ?? !BitConverter.IsLittleEndian ? s_big : Encoding), 4, true, int.MaxValue, requiredAlignment, allocator);

    /// <summary>
    /// Allocates a zero-terminated UTF-32 string with the CoTaskMem allocator.
    /// </summary>
    /// <param name="value">The string to encode, or null for a null native pointer.</param>
    /// <returns>An owned native pointer that must be released with Marshal.FreeCoTaskMem.</returns>
    /// <exception cref="EncoderFallbackException">The string contains invalid UTF-16.</exception>
    public static nint Allocate(string? value)
    {
        if (value is null)
            return 0;
        byte[] bytes = Encoding.GetBytes(value);
        nint address = Marshal.AllocCoTaskMem(checked(bytes.Length + sizeof(uint)));
        try
        {
            Marshal.Copy(bytes, 0, address, bytes.Length);
            *(uint*)((byte*)address + bytes.Length) = 0;
            return address;
        }
        catch
        {
            Marshal.FreeCoTaskMem(address);
            throw;
        }
    }

    /// <summary>
    /// Decodes a borrowed zero-terminated UTF-32 string within an explicit scan limit.
    /// </summary>
    /// <param name="address">Native memory valid through its terminator, or zero for null.</param>
    /// <param name="maximumCodeUnits">The scalar scan cap including the terminator; this does not prove the allocation's readable extent.</param>
    /// <param name="bigEndian">Explicit byte order, or null for the consuming process's native order.</param>
    /// <returns>The decoded string, or null for a null native pointer.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The scan limit is not positive.</exception>
    /// <exception cref="ArgumentException">The scan cap was reached before a terminator.</exception>
    /// <exception cref="DecoderFallbackException">The native string contains an invalid Unicode scalar.</exception>
    public static string? Decode(
        nint address,
        int maximumCodeUnits = 1048576,
        bool? bigEndian = null
    ) {
        int bytes = NativeTextReader.NullTerminatedByteLength(address, maximumCodeUnits, 4);
        return address == 0 ? null : (bigEndian ?? !BitConverter.IsLittleEndian ? s_big : Encoding).GetString(new ReadOnlySpan<byte>((void*)address, bytes));
    }

    /// <summary>Decodes an exact UTF-32 extent with strict scalar validation and embedded null preservation.</summary>
    /// <param name="address">Native memory valid for the specified extent, or zero for null.</param>
    /// <param name="codeUnitCount">The exact code-unit count excluding any trailing terminator.</param>
    /// <param name="bigEndian">The code-unit order, or null for native order.</param>
    /// <returns>The copied managed string, or null.</returns>
    public static string? DecodeLength(
        nint address,
        int codeUnitCount,
        bool? bigEndian = null
    ) {
        int bytes = NativeTextReader.ExactByteLength(address, codeUnitCount, 4);
        return address == 0 ? null : (bigEndian ?? !BitConverter.IsLittleEndian ? s_big : Encoding).GetString(new ReadOnlySpan<byte>((void*)address, bytes));
    }

    /// <summary>Encodes exact counted UTF-32 input, preserving embedded zero code units.</summary>
    /// <param name="value">The Unicode input; null has address and count zero when allowed.</param>
    /// <param name="allowsNull">Whether the native contract permits null.</param>
    /// <param name="bigEndian">The code-unit byte order, or null for native order.</param>
    /// <param name="requiredAlignment">The exact native code-unit alignment.</param>
    /// <param name="maximumCodeUnits">The positive encoded code-unit bound, excluding extra termination.</param>
    /// <returns>Temporary native text with its exact count; the caller disposes the lease.</returns>
    public static NativeStringLease EncodeCounted(
        string? value,
        bool allowsNull,
        bool? bigEndian = null,
        nuint requiredAlignment = 4,
        int maximumCodeUnits = 1048576
    )
        => EncodeCounted(value, allowsNull, bigEndian, requiredAlignment, allocator: null, maximumCodeUnits: maximumCodeUnits);

    /// <summary>Encodes counted UTF-32 through the matching allocation pair without truncating embedded zeros.</summary>
    /// <param name="value">The Unicode input, or null only when declared.</param>
    /// <param name="allowsNull">The native null-pointer contract.</param>
    /// <param name="bigEndian">The code-unit byte order, or null for native order.</param>
    /// <param name="requiredAlignment">The requested native code-unit alignment.</param>
    /// <param name="allocator">The matching allocation pair, or null for the framework allocator.</param>
    /// <param name="maximumCodeUnits">The positive encoded code-unit bound, excluding extra termination.</param>
    /// <returns>A temporary lease retaining its allocator and exact encoded code-unit count.</returns>
    public static NativeStringLease EncodeCounted(
        string? value,
        bool allowsNull,
        bool? bigEndian,
        nuint requiredAlignment,
        NativeAllocator? allocator,
        int maximumCodeUnits = 1048576
    )
        => NativeTextEncoder.Encode(value, allowsNull, (bigEndian ?? !BitConverter.IsLittleEndian ? s_big : Encoding), 4, false, maximumCodeUnits, requiredAlignment, allocator);
}
