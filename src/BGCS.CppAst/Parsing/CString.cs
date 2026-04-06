// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.


using BGCS.CppAst.Utilities;
using System;
using System.Runtime.CompilerServices;

namespace BGCS.CppAst.Parsing;
/// <summary>
/// Defines the public struct <c>CString</c>.
/// </summary>
public readonly unsafe struct CString : IEquatable<CString>
{
    private readonly byte* data;
    private readonly int length;

    /// <summary>
    /// Initializes a new instance of <see cref="CString"/>.
    /// </summary>
    public CString(byte* data, int length)
    {
        this.data = data;
        this.length = length;
    }

    /// <summary>
    /// Exposes public member <c>data</c>.
    /// </summary>
    public readonly byte* CStr => data;

    /// <summary>
    /// Exposes public member <c>length</c>.
    /// </summary>
    public readonly int Length => length;

    /// <summary>
    /// Executes public operation <c>AsSpan</c>.
    /// </summary>
    public readonly Span<byte> AsSpan() => new(data, length);

    /// <summary>
    /// Executes public operation <c>Equals</c>.
    /// </summary>
    public override readonly bool Equals(object? obj)
    {
        return obj is CString @string && Equals(@string);
    }

    /// <summary>
    /// Executes public operation <c>Equals</c>.
    /// </summary>
    public readonly bool Equals(CString other)
    {
        return StrCmp(data, other.data) == 0;
    }

    /// <summary>
    /// Returns computed data from <c>GetHashCode</c>.
    /// </summary>
    public override readonly int GetHashCode()
    {
        return (int)MurmurHash3.Hash32(AsSpan());
    }

    /// <summary>
    /// Executes public operation <c>Member</c>.
    /// </summary>
    public static bool operator ==(CString left, CString right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Executes public operation <c>Member</c>.
    /// </summary>
    public static bool operator !=(CString left, CString right)
    {
        return !(left == right);
    }

    /// <summary>
    /// Executes public operation <c>Span</c>.
    /// </summary>
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
