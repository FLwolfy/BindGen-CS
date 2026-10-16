using System;
using System.Text;

namespace BGCS.Runtime;

/// <summary>Converts explicitly declared UTF-16 text using strict Unicode and an explicit code-unit byte order.</summary>
public static class Utf16Codec
{
    private static readonly UnicodeEncoding s_little = new(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true);
    private static readonly UnicodeEncoding s_big = new(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true);

    /// <summary>Allocates aligned, zero-terminated UTF-16 input for one nonretaining native call.</summary>
    /// <param name="value">The managed Unicode input.</param>
    /// <param name="allowsNull">Whether native code permits a null text address.</param>
    /// <param name="bigEndian">Explicit byte order, or null for the consuming process's native order.</param>
    /// <param name="requiredAlignment">The declared native pointee alignment.</param>
    /// <returns>The independently owned temporary native text.</returns>
    /// <exception cref="ArgumentException">Embedded null code units would truncate the declared C string.</exception>
    /// <exception cref="ArgumentNullException">The native contract forbids null.</exception>
    /// <exception cref="EncoderFallbackException">The managed input contains invalid Unicode.</exception>
    public static NativeStringLease EncodeNullTerminated(
        string? value,
        bool allowsNull,
        bool? bigEndian = null,
        nuint requiredAlignment = 2
    )
        => EncodeNullTerminated(value, allowsNull, bigEndian, requiredAlignment, allocator: null);

    /// <summary>Encodes nonretained UTF-16 input through an explicit allocator/free pair and code-unit byte order.</summary>
    /// <param name="value">The managed Unicode input.</param>
    /// <param name="allowsNull">The native null-pointer contract.</param>
    /// <param name="bigEndian">The code-unit byte order, or null for native order.</param>
    /// <param name="requiredAlignment">The declared native pointee alignment.</param>
    /// <param name="allocator">The custom allocation pair, or null for unchanged framework allocation.</param>
    /// <returns>Temporary text retaining the actual matching allocator and optional module.</returns>
    public static unsafe NativeStringLease EncodeNullTerminated(
        string? value,
        bool allowsNull,
        bool? bigEndian,
        nuint requiredAlignment,
        NativeAllocator? allocator
    )
        => NativeTextEncoder.Encode(value, allowsNull, Encoding(bigEndian), 2, true, int.MaxValue, requiredAlignment, allocator);

    /// <summary>Copies strictly decoded UTF-16 after a scalar scan stopping at the first zero code unit.</summary>
    /// <param name="address">Native memory valid through its terminator, or zero for null.</param>
    /// <param name="maximumCodeUnits">The scan cap including the terminator; this does not establish readable allocation size.</param>
    /// <param name="bigEndian">Explicit byte order, or null for the consuming process's native order.</param>
    /// <returns>The decoded text, or null for a null native address.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The scan cap is nonpositive.</exception>
    /// <exception cref="ArgumentException">The scan cap was reached without a terminator.</exception>
    /// <exception cref="DecoderFallbackException">The native text contains invalid Unicode.</exception>
    public static unsafe string? Decode(
        nint address,
        int maximumCodeUnits = 1048576,
        bool? bigEndian = null
    ) {
        int bytes = NativeTextReader.NullTerminatedByteLength(address, maximumCodeUnits, 2);
        return address == 0 ? null : Encoding(bigEndian).GetString(new ReadOnlySpan<byte>((void*)address, bytes));
    }

    /// <summary>Decodes an exact UTF-16 extent with strict surrogate validation and embedded null preservation.</summary>
    /// <param name="address">Native memory valid for the specified extent, or zero for null.</param>
    /// <param name="codeUnitCount">The exact code-unit count excluding any trailing terminator.</param>
    /// <param name="bigEndian">The code-unit order, or null for native order.</param>
    /// <returns>The copied managed string, or null.</returns>
    public static unsafe string? DecodeLength(
        nint address,
        int codeUnitCount,
        bool? bigEndian = null
    ) {
        int bytes = NativeTextReader.ExactByteLength(address, codeUnitCount, 2);
        return address == 0 ? null : Encoding(bigEndian).GetString(new ReadOnlySpan<byte>((void*)address, bytes));
    }

    private static UnicodeEncoding Encoding(bool? bigEndian) => bigEndian ?? !BitConverter.IsLittleEndian ? s_big : s_little;

    /// <summary>Encodes exact counted UTF-16 input, preserving embedded zero code units.</summary>
    /// <param name="value">The Unicode input; null produces a null address and count zero when allowed.</param>
    /// <param name="allowsNull">Whether the native contract permits null.</param>
    /// <param name="bigEndian">The code-unit byte order, or null for the native order.</param>
    /// <param name="requiredAlignment">The exact native code-unit alignment.</param>
    /// <param name="maximumCodeUnits">The positive bound for encoded code units, excluding extra termination.</param>
    /// <returns>Temporary native text with its exact count; the caller disposes the lease.</returns>
    public static NativeStringLease EncodeCounted(
        string? value,
        bool allowsNull,
        bool? bigEndian = null,
        nuint requiredAlignment = 2,
        int maximumCodeUnits = 1048576
    )
        => EncodeCounted(value, allowsNull, bigEndian, requiredAlignment, allocator: null, maximumCodeUnits: maximumCodeUnits);

    /// <summary>Encodes counted UTF-16 through the matching allocation pair without truncating embedded zeros.</summary>
    /// <param name="value">The Unicode input, or null only when declared.</param>
    /// <param name="allowsNull">The native null-pointer contract.</param>
    /// <param name="bigEndian">The code-unit byte order, or null for the native order.</param>
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
        => NativeTextEncoder.Encode(value, allowsNull, Encoding(bigEndian), 2, false, maximumCodeUnits, requiredAlignment, allocator);
}
