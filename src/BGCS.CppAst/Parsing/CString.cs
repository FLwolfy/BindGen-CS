// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.


using BGCS.CppAst.Utilities;
using System;
using System.Runtime.CompilerServices;

namespace BGCS.CppAst.Parsing;
public readonly unsafe struct CString : IEquatable<CString>
{
    private readonly byte* data;
    private readonly int length;

    public CString(byte* data, int length)
    {
        this.data = data;
        this.length = length;
    }

    public readonly byte* CStr => data;

    public readonly int Length => length;

    public readonly Span<byte> AsSpan() => new(data, length);

    public override readonly bool Equals(object? obj)
    {
        return obj is CString @string && Equals(@string);
    }

    public readonly bool Equals(CString other)
    {
        return StrCmp(data, other.data) == 0;
    }

    public override readonly int GetHashCode()
    {
        return (int)MurmurHash3.Hash32(AsSpan());
    }

    public static bool operator ==(CString left, CString right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(CString left, CString right)
    {
        return !(left == right);
    }

    public static implicit operator Span<byte>(CString str) => str.AsSpan();
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe int StrCmp(byte* a, byte* b)
    {
        if ((IntPtr) a == IntPtr.Zero)
            return (IntPtr) b != IntPtr.Zero ? -1 : 0;
        if ((IntPtr) b == IntPtr.Zero)
            return 1;
        for (; *a != (byte) 0 && *b != (byte) 0; ++b)
        {
            if ((int) *a != (int) *b)
                return (int) *a - (int) *b;
            ++a;
        }
        if (*a == (byte) 0 && *b == (byte) 0)
            return 0;
        return *a != (byte) 0 ? 1 : -1;
    }
}
