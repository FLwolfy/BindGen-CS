using System;
using System.Runtime.CompilerServices;
using ClangSharp.Interop;

namespace BGCS.CppAst.Parsing;
public struct CursorKey : IEquatable<CursorKey>
{
    public ResolverScope scope;
    public CXCursor cursor;
    public CString name;

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

    public override readonly bool Equals(object? obj)
    {
        return obj is CursorKey key && Equals(key);
    }

    public readonly bool Equals(CursorKey other)
    {
        return other.scope == scope && other.name == name && other.cursor.IsAnonymous == cursor.IsAnonymous && !(cursor.IsAnonymous && cursor.Hash != other.cursor.Hash);
    }

    public override readonly int GetHashCode()
    {
        return HashCode.Combine(scope, name.GetHashCode(), cursor.IsAnonymous, cursor.IsAnonymous ? cursor.Hash : 0);
    }

    public static bool operator ==(CursorKey left, CursorKey right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(CursorKey left, CursorKey right)
    {
        return !(left == right);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe int MbStrLen(byte* ptr)
    {
        if ((IntPtr) ptr == IntPtr.Zero)
            return 0;
        int num1 = 0;
        while (*ptr != (byte) 0)
        {
            byte num2 = *ptr;
            if (((int) num2 & 128 /*0x80*/) == 0)
                ++ptr;
            else if (((int) num2 & 224 /*0xE0*/) == 192 /*0xC0*/)
            {
                if (((int) ptr[1] & 192 /*0xC0*/) != 128 /*0x80*/)
                    return -1;
                ptr += 2;
            }
            else if (((int) num2 & 240 /*0xF0*/) == 224 /*0xE0*/)
            {
                if (((int) ptr[1] & 192 /*0xC0*/) != 128 /*0x80*/ || ((int) ptr[2] & 192 /*0xC0*/) != 128 /*0x80*/)
                    return -1;
                ptr += 3;
            }
            else
            {
                if (((int) num2 & 248) != 240 /*0xF0*/ || ((int) ptr[1] & 192 /*0xC0*/) != 128 /*0x80*/ || ((int) ptr[2] & 192 /*0xC0*/) != 128 /*0x80*/ || ((int) ptr[3] & 192 /*0xC0*/) != 128 /*0x80*/)
                    return -1;
                ptr += 4;
            }
            ++num1;
        }
        return num1;
    }
}
