using System;
using System.Threading;

namespace BGCS.Runtime;

internal static unsafe class NativeTextReader
{
    internal static int NullTerminatedByteLength(
        nint address,
        int maximumCodeUnits,
        int width
    ) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCodeUnits);
        if (address == 0) return 0;
        for (int length = 0; length < maximumCodeUnits; length++)
        {
            nuint current = checked(unchecked((nuint)address) + checked((nuint)length * (nuint)width));
            bool zero = true;
            // Individual volatile byte reads cannot speculate a vector load
            // beyond the first zero code unit at a native allocation boundary.
            for (int part = 0; part < width; part++)
                zero &= Volatile.Read(ref *(byte*)checked(current + (nuint)part)) == 0;
            if (zero) return checked(length * width);
        }
        throw new ArgumentException("No native text terminator exists within the configured code-unit scan limit.", nameof(address));
    }

    internal static int ExactByteLength(
        nint address,
        int codeUnitCount,
        int width
    ) {
        ArgumentOutOfRangeException.ThrowIfNegative(codeUnitCount);
        int bytes = checked(codeUnitCount * width);
        if (address != 0 && bytes != 0) _ = checked(unchecked((nuint)address) + (nuint)(bytes - 1));
        return bytes;
    }
}
