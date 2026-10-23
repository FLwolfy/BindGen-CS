using System;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Generic;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using ClangSharp.Interop;

namespace BGCS.Frontend.Clang.Model.Types;

/// <summary>
/// A C++ primitive type (e.g `int`, `void`, `bool`...)
/// </summary>
internal sealed class CppPrimitiveType : CppType
{
    private static readonly ConcurrentDictionary<(CppPrimitiveKind Kind, int Size, string? Spelling), CppPrimitiveType> AbiSizedTypes = new();
    /// <summary>
    /// Singleton instance of the `void` type.
    /// </summary>
    public static readonly CppPrimitiveType @void = new CppPrimitiveType(CppPrimitiveKind.Void);
    /// <summary>
    /// Singleton instance of the `bool` type.
    /// </summary>
    public static readonly CppPrimitiveType @bool = new CppPrimitiveType(CppPrimitiveKind.Bool);
    /// <summary>
    /// Singleton instance of the `wchar` type.
    /// </summary>
    public static readonly CppPrimitiveType wChar = new CppPrimitiveType(CppPrimitiveKind.WChar);
    /// <summary>Gets the distinct C++ char16_t code-unit primitive.</summary>
    public static readonly CppPrimitiveType char16 = new(CppPrimitiveKind.Char16);
    /// <summary>Gets the distinct C++ char32_t code-unit primitive.</summary>
    public static readonly CppPrimitiveType char32 = new(CppPrimitiveKind.Char32);
    /// <summary>Gets the distinct C++ char8_t code-unit primitive.</summary>
    public static readonly CppPrimitiveType char8 = new(CppPrimitiveKind.Char8);
    /// <summary>
    /// Singleton instance of the `char` type.
    /// </summary>
    public static readonly CppPrimitiveType @char = new CppPrimitiveType(CppPrimitiveKind.Char);
    // C and C++ give plain char and signed char distinct type identities even
    // on a target where plain char is signed. Never share their storage facts.
    internal static readonly CppPrimitiveType signedChar = new(CppPrimitiveKind.Char, 1, "signed char");
    /// <summary>
    /// Singleton instance of the `short` type.
    /// </summary>
    public static readonly CppPrimitiveType @short = new CppPrimitiveType(CppPrimitiveKind.Short);
    /// <summary>
    /// Singleton instance of the `int` type.
    /// </summary>
    public static readonly CppPrimitiveType @int = new CppPrimitiveType(CppPrimitiveKind.Int);
    /// <summary>
    /// Singleton instance of the `long` type.
    /// </summary>
    public static readonly CppPrimitiveType @long = new CppPrimitiveType(CppPrimitiveKind.Long);
    /// <summary>
    /// Singleton instance of the `long long` type.
    /// </summary>
    public static readonly CppPrimitiveType longLong = new CppPrimitiveType(CppPrimitiveKind.LongLong);
    /// <summary>
    /// Singleton instance of the `unsigned char` type.
    /// </summary>
    public static readonly CppPrimitiveType unsignedChar = new CppPrimitiveType(CppPrimitiveKind.UnsignedChar);
    /// <summary>
    /// Singleton instance of the `unsigned short` type.
    /// </summary>
    public static readonly CppPrimitiveType unsignedShort = new CppPrimitiveType(CppPrimitiveKind.UnsignedShort);
    /// <summary>
    /// Singleton instance of the `unsigned int` type.
    /// </summary>
    public static readonly CppPrimitiveType unsignedInt = new CppPrimitiveType(CppPrimitiveKind.UnsignedInt);
    /// <summary>
    /// Singleton instance of the `unsigned long` type.
    /// </summary>
    public static readonly CppPrimitiveType unsignedLong = new CppPrimitiveType(CppPrimitiveKind.UnsignedLong);
    /// <summary>
    /// Singleton instance of the `unsigned long long` type.
    /// </summary>
    public static readonly CppPrimitiveType unsignedLongLong = new CppPrimitiveType(CppPrimitiveKind.UnsignedLongLong);
    /// <summary>
    /// Singleton instance of the `float` type.
    /// </summary>
    public static readonly CppPrimitiveType @float = new CppPrimitiveType(CppPrimitiveKind.Float);
    /// <summary>
    /// Singleton instance of the `float` type.
    /// </summary>
    public static readonly CppPrimitiveType @double = new CppPrimitiveType(CppPrimitiveKind.Double);
    /// <summary>
    /// Singleton instance of the `long double` type.
    /// </summary>
    public static readonly CppPrimitiveType longDouble = new CppPrimitiveType(CppPrimitiveKind.LongDouble);
    /// <summary>
    /// Unsigned 128 bits integer type.
    /// </summary>
    public static readonly CppPrimitiveType uInt128 = new CppPrimitiveType(CppPrimitiveKind.UInt128);
    /// <summary>
    /// 128 bits integer type.
    /// </summary>
    public static readonly CppPrimitiveType int128 = new CppPrimitiveType(CppPrimitiveKind.Int128);
    /// <summary>
    /// Float16 type.
    /// </summary>
    public static readonly CppPrimitiveType float16 = new CppPrimitiveType(CppPrimitiveKind.Float16);
    /// <summary>
    /// BFloat16 type.
    /// </summary>
    public static readonly CppPrimitiveType bFloat16 = new CppPrimitiveType(CppPrimitiveKind.BFloat16);
    private readonly int m_sizeOf;
    private readonly string? m_spelling;
    /// <summary>
    /// Base constructor of a primitive
    /// </summary>
    /// <param name = "kind"></param>
    private CppPrimitiveType(CppPrimitiveKind kind) : base(default, CppTypeKind.Primitive)
    {
        this.kind = kind;
        UpdateSize(out this.m_sizeOf);
    }

    private CppPrimitiveType(
        CppPrimitiveKind kind,
        int sizeOf,
        string? spelling = null
    ) : base(default, CppTypeKind.Primitive)
    {
        this.kind = kind;
        this.m_sizeOf = sizeOf;
        m_spelling = spelling;
    }

    /// <summary>
    /// Returns the canonical primitive instance for an ABI-reported size.
    /// </summary>
    /// <remarks>
    /// Clang target triples can change the width of primitives such as <c>long</c>,
    /// <c>wchar_t</c>, and <c>long double</c>. The historical static instances remain
    /// canonical for their original sizes; target-specific variants are cached.
    /// </remarks>
    internal static CppPrimitiveType ForAbiSize(
        CppPrimitiveType primitiveType,
        long sizeOf
    ) {
        ArgumentNullException.ThrowIfNull(primitiveType);
        if (sizeOf <= 0 || sizeOf == primitiveType.sizeOf)
            return primitiveType;
        int size = checked((int)sizeOf);
        return AbiSizedTypes.GetOrAdd((primitiveType.kind, size, primitiveType.m_spelling), static key => new CppPrimitiveType(key.Kind, key.Size, key.Spelling));
    }

    /// <summary>
    /// The kind of primitive.
    /// </summary>
    public CppPrimitiveKind kind { get; }

    private void UpdateSize(out int sizeOf)
    {
        switch (this.kind)
        {
            case CppPrimitiveKind.Void:
                sizeOf = 0;
                break;
            case CppPrimitiveKind.Bool:
                sizeOf = 1;
                break;
            case CppPrimitiveKind.WChar:
            case CppPrimitiveKind.Char16:
                sizeOf = 2;
                break;
            case CppPrimitiveKind.Char32:
                sizeOf = 4;
                break;
            case CppPrimitiveKind.Char:
            case CppPrimitiveKind.Char8:
                sizeOf = 1;
                break;
            case CppPrimitiveKind.Short:
                sizeOf = 2;
                break;
            case CppPrimitiveKind.Int:
                sizeOf = 4;
                break;
            case CppPrimitiveKind.Long:
            case CppPrimitiveKind.UnsignedLong:
                sizeOf = 4;
                break;
            case CppPrimitiveKind.LongLong:
                sizeOf = 8;
                break;
            case CppPrimitiveKind.UnsignedChar:
                sizeOf = 1;
                break;
            case CppPrimitiveKind.UnsignedShort:
                sizeOf = 2;
                break;
            case CppPrimitiveKind.UnsignedInt:
                sizeOf = 4;
                break;
            case CppPrimitiveKind.UnsignedLongLong:
                sizeOf = 8;
                break;
            case CppPrimitiveKind.Float:
                sizeOf = 4;
                break;
            case CppPrimitiveKind.Double:
                sizeOf = 8;
                break;
            case CppPrimitiveKind.LongDouble:
                sizeOf = 8;
                break;
            case CppPrimitiveKind.UInt128:
            case CppPrimitiveKind.Int128:
                sizeOf = 16;
                break;
            case CppPrimitiveKind.Float16:
            case CppPrimitiveKind.BFloat16:
                sizeOf = 2;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return m_spelling ?? (this.kind switch
        {
            CppPrimitiveKind.Void => "void",
            CppPrimitiveKind.WChar => "wchar_t",
            CppPrimitiveKind.Char16 => "char16_t",
            CppPrimitiveKind.Char32 => "char32_t",
            CppPrimitiveKind.Char8 => "char8_t",
            CppPrimitiveKind.Char => "char",
            CppPrimitiveKind.Short => "short",
            CppPrimitiveKind.Int => "int",
            CppPrimitiveKind.Long => "long",
            CppPrimitiveKind.UnsignedLong => "unsigned long",
            CppPrimitiveKind.LongLong => "long long",
            CppPrimitiveKind.UnsignedChar => "unsigned char",
            CppPrimitiveKind.UnsignedShort => "unsigned short",
            CppPrimitiveKind.UnsignedInt => "unsigned int",
            CppPrimitiveKind.UnsignedLongLong => "unsigned long long",
            CppPrimitiveKind.Float => "float",
            CppPrimitiveKind.Double => "double",
            CppPrimitiveKind.LongDouble => "long double",
            CppPrimitiveKind.Bool => "bool",
            CppPrimitiveKind.Int128 => "System.Int128",
            CppPrimitiveKind.UInt128 => "System.UInt128",
            CppPrimitiveKind.Float16 => "System.Half",
            CppPrimitiveKind.BFloat16 => "BFloat16",
            _ => throw new InvalidOperationException($"Unhandled PrimitiveKind: {this.kind}")
        });
    }

    private bool Equals(CppPrimitiveType other)
    {
        return base.Equals(other) && this.kind == other.kind;
    }

    /// <inheritdoc/>
    public override int sizeOf { get => this.m_sizeOf; set => throw new InvalidOperationException("Cannot set the SizeOf of a primitive type"); }

    /// <inheritdoc/>
    public override CppType GetCanonicalType()
    {
        return this;
    }

    /// <summary>
    /// Provides native primitive categories with baseline carriers; parsing selects ABI-sized instances for the actual target.
    /// </summary>
    public static readonly FrozenDictionary<CXTypeKind, CppPrimitiveType> kindToPrimitive = new Dictionary<CXTypeKind, CppPrimitiveType>()
    {
        {
            CXTypeKind.CXType_Void,
            global::BGCS.Frontend.Clang.Model.Types.CppPrimitiveType.@void
        },
        {
            CXTypeKind.CXType_Bool,
            global::BGCS.Frontend.Clang.Model.Types.CppPrimitiveType.@bool
        },
        {
            CXTypeKind.CXType_UChar,
            global::BGCS.Frontend.Clang.Model.Types.CppPrimitiveType.unsignedChar
        },
        {
            CXTypeKind.CXType_UShort,
            global::BGCS.Frontend.Clang.Model.Types.CppPrimitiveType.unsignedShort
        },
        {
            CXTypeKind.CXType_UInt,
            global::BGCS.Frontend.Clang.Model.Types.CppPrimitiveType.unsignedInt
        },
        {
            CXTypeKind.CXType_ULong,
            global::BGCS.Frontend.Clang.Model.Types.CppPrimitiveType.unsignedLong
        },
        {
            CXTypeKind.CXType_ULongLong,
            global::BGCS.Frontend.Clang.Model.Types.CppPrimitiveType.unsignedLongLong
        },
        {
            CXTypeKind.CXType_SChar,
            global::BGCS.Frontend.Clang.Model.Types.CppPrimitiveType.signedChar
        },
        // Plain char follows the target ABI's default signedness. It remains the
        // C/C++ char type in the binding model rather than becoming unsigned byte.
        {
            CXTypeKind.CXType_Char_U,
            global::BGCS.Frontend.Clang.Model.Types.CppPrimitiveType.@char
        },
        {
            CXTypeKind.CXType_Char_S,
            global::BGCS.Frontend.Clang.Model.Types.CppPrimitiveType.@char
        },
        {
            CXTypeKind.CXType_WChar,
            global::BGCS.Frontend.Clang.Model.Types.CppPrimitiveType.wChar
        },
        {
            CXTypeKind.CXType_Char16,
            global::BGCS.Frontend.Clang.Model.Types.CppPrimitiveType.char16
        },
        {
            CXTypeKind.CXType_Char32,
            global::BGCS.Frontend.Clang.Model.Types.CppPrimitiveType.char32
        },
        {
            CXTypeKind.CXType_Short,
            global::BGCS.Frontend.Clang.Model.Types.CppPrimitiveType.@short
        },
        {
            CXTypeKind.CXType_Int,
            global::BGCS.Frontend.Clang.Model.Types.CppPrimitiveType.@int
        },
        {
            CXTypeKind.CXType_Long,
            global::BGCS.Frontend.Clang.Model.Types.CppPrimitiveType.@long
        },
        {
            CXTypeKind.CXType_LongLong,
            global::BGCS.Frontend.Clang.Model.Types.CppPrimitiveType.longLong
        },
        {
            CXTypeKind.CXType_Float,
            global::BGCS.Frontend.Clang.Model.Types.CppPrimitiveType.@float
        },
        {
            CXTypeKind.CXType_Double,
            global::BGCS.Frontend.Clang.Model.Types.CppPrimitiveType.@double
        },
        {
            CXTypeKind.CXType_LongDouble,
            global::BGCS.Frontend.Clang.Model.Types.CppPrimitiveType.longDouble
        },
        {
            CXTypeKind.CXType_Int128,
            global::BGCS.Frontend.Clang.Model.Types.CppPrimitiveType.int128
        },
        {
            CXTypeKind.CXType_UInt128,
            global::BGCS.Frontend.Clang.Model.Types.CppPrimitiveType.uInt128
        },
        {
            CXTypeKind.CXType_Float16,
            global::BGCS.Frontend.Clang.Model.Types.CppPrimitiveType.float16
        },
        {
            CXTypeKind.CXType_BFloat16,
            global::BGCS.Frontend.Clang.Model.Types.CppPrimitiveType.bFloat16
        },
    }.ToFrozenDictionary();
}
