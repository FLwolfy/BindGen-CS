using System;
using System.Runtime.CompilerServices;
using ClangSharp.Interop;

namespace BGCS.CppAst.Parsing;

/// <summary>
/// Defines the public struct <c>CursorKey</c>.
/// </summary>
public struct CursorKey : IEquatable<CursorKey>
{
    /// <summary>
    /// Exposes public member <c>scope</c>.
    /// </summary>
    public ResolverScope scope;
    /// <summary>
    /// Exposes public member <c>cursor</c>.
    /// </summary>
    public CXCursor cursor;
    /// <summary>
    /// Exposes public member <c>name</c>.
    /// </summary>
    public CString name;

    /// <summary>
    /// Initializes a new instance of <see cref="CursorKey"/>.
    /// </summary>
    public unsafe CursorKey(CppContainerContext context, CXCursor cursor)
    {
        scope = context.Type switch
        {
            CppContainerContextType.Unspecified => ResolverScope.System,
            CppContainerContextType.System => ResolverScope.System,
            CppContainerContextType.User => ResolverScope.User,
            _ => ResolverScope.System,
        };

        while (cursor.Kind == CXCursorKind.CXCursor_LinkageSpec)
        {
            cursor = cursor.SemanticParent;
        }
        this.cursor = cursor;

        using var usr = cursor.Usr;
        var usrCstr = (byte*)clang.getCString(usr);
        var usrLen = MbStrLen(usrCstr);
        if (usrLen == -1) throw new Exception();
        if (usrLen == 0)
        {
            using var displayName = cursor.DisplayName;
            var displayNameCstr = (byte*)clang.getCString(displayName);
            var displayNameLen = MbStrLen(displayNameCstr);
            if (displayNameLen == -1) throw new Exception();
            name = new(BumpAllocator.Shared.Alloc((nuint)displayNameLen + 1), displayNameLen);
            Buffer.MemoryCopy(displayNameCstr, name.CStr, displayNameLen + 1, displayNameLen + 1);
        }
        else
        {
            name = new(BumpAllocator.Shared.Alloc((nuint)usrLen + 1), usrLen);
            Buffer.MemoryCopy(usrCstr, name.CStr, usrLen + 1, usrLen + 1);
        }
    }

    /// <summary>
    /// Executes public operation <c>Equals</c>.
    /// </summary>
    public override readonly bool Equals(object? obj) => obj is CursorKey key && Equals(key);

    /// <summary>
    /// Executes public operation <c>Equals</c>.
    /// </summary>
    public readonly bool Equals(CursorKey other)
    {
        return other.scope == scope && other.name == name && other.cursor.IsAnonymous == cursor.IsAnonymous &&
            !(cursor.IsAnonymous && cursor.Hash != other.cursor.Hash);
    }

    /// <summary>
    /// Returns computed data from <c>GetHashCode</c>.
    /// </summary>
    public override readonly int GetHashCode()
    {
        return HashCode.Combine(scope, name.GetHashCode(), cursor.IsAnonymous, cursor.IsAnonymous ? cursor.Hash : 0);
    }

    /// <summary>
    /// Executes public operation <c>Member</c>.
    /// </summary>
    public static bool operator ==(CursorKey left, CursorKey right) => left.Equals(right);

    /// <summary>
    /// Executes public operation <c>Member</c>.
    /// </summary>
    public static bool operator !=(CursorKey left, CursorKey right) => !left.Equals(right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe int MbStrLen(byte* ptr)
    {
        if ((IntPtr)ptr == IntPtr.Zero)
            return 0;
        int num1 = 0;
        while (*ptr != 0)
        {
            byte num2 = *ptr;
            if ((num2 & 128) == 0)
                ++ptr;
            else if ((num2 & 224) == 192)
            {
                if ((ptr[1] & 192) != 128)
                    return -1;
                ptr += 2;
            }
            else if ((num2 & 240) == 224)
            {
                if ((ptr[1] & 192) != 128 || (ptr[2] & 192) != 128)
                    return -1;
                ptr += 3;
            }
            else
            {
                if ((num2 & 248) != 240 || (ptr[1] & 192) != 128 || (ptr[2] & 192) != 128 || (ptr[3] & 192) != 128)
                    return -1;
                ptr += 4;
            }
            ++num1;
        }
        return num1;
    }
}
