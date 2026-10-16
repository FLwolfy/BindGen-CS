using System;
using System.Text;

namespace BGCS.Runtime;

/// <summary>Converts explicitly declared UTF-8 call inputs with strict Unicode and zero-termination rules.</summary>
public static class Utf8Codec
{
    private static readonly UTF8Encoding s_encoding = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Allocates UTF-8 input whose lifetime is bounded by one nonretaining native call.</summary>
    /// <param name="value">The managed text; null is allowed only when declared.</param>
    /// <param name="allowsNull">Whether the native contract accepts a null pointer.</param>
    /// <param name="requiredAlignment">The declared native pointee alignment, defaulting to one byte.</param>
    /// <returns>An independently owned, zero-terminated native text lease.</returns>
    /// <exception cref="ArgumentNullException">Null is not permitted by the contract.</exception>
    /// <exception cref="ArgumentException">Embedded zero code units would truncate the declared C string.</exception>
    /// <exception cref="EncoderFallbackException">The input contains invalid Unicode.</exception>
    public static NativeStringLease EncodeNullTerminated(
        string? value,
        bool allowsNull,
        nuint requiredAlignment = 1
    )
        => EncodeNullTerminated(value, allowsNull, requiredAlignment, allocator: null);

    /// <summary>Encodes nonretained UTF-8 input through an explicit allocator/free pair or the framework default.</summary>
    /// <param name="value">The managed text; null is accepted only when declared.</param>
    /// <param name="allowsNull">The native null-pointer contract.</param>
    /// <param name="requiredAlignment">The exact requested native pointee alignment.</param>
    /// <param name="allocator">The custom allocation pair, or null for unchanged framework allocation.</param>
    /// <returns>Temporary text retaining the actual matching allocator and optional module.</returns>
    public static unsafe NativeStringLease EncodeNullTerminated(
        string? value,
        bool allowsNull,
        nuint requiredAlignment,
        NativeAllocator? allocator
    )
        => NativeTextEncoder.Encode(value, allowsNull, s_encoding, 1, true, int.MaxValue, requiredAlignment, allocator);

    /// <summary>Decodes strict UTF-8 after a scalar zero-termination scan bounded in native code units.</summary>
    /// <param name="address">Native memory valid through its terminator, or zero for null.</param>
    /// <param name="maximumCodeUnits">The scan cap; it does not establish readable allocation size.</param>
    /// <returns>A copied managed string, or null.</returns>
    public static unsafe string? Decode(
        nint address,
        int maximumCodeUnits = 1048576
    ) {
        int bytes = NativeTextReader.NullTerminatedByteLength(address, maximumCodeUnits, 1);
        return address == 0 ? null : s_encoding.GetString(new ReadOnlySpan<byte>((void*)address, bytes));
    }

    /// <summary>Decodes an exact UTF-8 extent, preserving embedded null code units.</summary>
    /// <param name="address">Native memory valid for the specified extent, or zero for null.</param>
    /// <param name="codeUnitCount">The exact byte/code-unit count excluding any trailing terminator.</param>
    /// <returns>The copied managed string, or null.</returns>
    public static unsafe string? DecodeLength(
        nint address,
        int codeUnitCount
    ) {
        int bytes = NativeTextReader.ExactByteLength(address, codeUnitCount, 1);
        return address == 0 ? null : s_encoding.GetString(new ReadOnlySpan<byte>((void*)address, bytes));
    }

    /// <summary>Encodes exact counted UTF-8 input, preserving embedded zero code units.</summary>
    /// <param name="value">The Unicode input; null produces a null address and count zero when allowed.</param>
    /// <param name="allowsNull">Whether the native contract permits null.</param>
    /// <param name="requiredAlignment">The exact native code-unit alignment.</param>
    /// <param name="maximumCodeUnits">The positive bound for encoded code units, excluding extra termination.</param>
    /// <returns>Temporary native text with its exact count; the caller disposes the lease.</returns>
    public static NativeStringLease EncodeCounted(
        string? value,
        bool allowsNull,
        nuint requiredAlignment = 1,
        int maximumCodeUnits = 1048576
    )
        => EncodeCounted(value, allowsNull, requiredAlignment, allocator: null, maximumCodeUnits: maximumCodeUnits);

    /// <summary>Encodes counted UTF-8 through the matching allocation pair without truncating embedded zeros.</summary>
    /// <param name="value">The Unicode input, or null only when declared.</param>
    /// <param name="allowsNull">The native null-pointer contract.</param>
    /// <param name="requiredAlignment">The requested native code-unit alignment.</param>
    /// <param name="allocator">The matching allocation pair, or null for the framework allocator.</param>
    /// <param name="maximumCodeUnits">The positive encoded code-unit bound, excluding extra termination.</param>
    /// <returns>A temporary lease retaining its allocator and exact encoded code-unit count.</returns>
    public static NativeStringLease EncodeCounted(
        string? value,
        bool allowsNull,
        nuint requiredAlignment,
        NativeAllocator? allocator,
        int maximumCodeUnits = 1048576
    )
        => NativeTextEncoder.Encode(value, allowsNull, s_encoding, 1, false, maximumCodeUnits, requiredAlignment, allocator);
}
