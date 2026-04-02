// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

using ClangSharp.Interop;
using BGCS.CppAst.Extensions;
using System;

namespace BGCS.CppAst.Model.Types;
/// <summary>
/// A C++ array (e.g int[5] or int[])
/// </summary>
public sealed class CppArrayType : CppTypeWithElementType
{
    /// <summary>
    /// Constructor of a C++ array.
    /// </summary>
    /// <param name="cursor"></param>
    /// <param name="elementType">The element type (e.g `int`)</param>
    /// <param name="size">The size of the array. 0 means an unbound array</param>
    public CppArrayType(CXCursor cursor, CppType elementType, int size) : base(cursor, CppTypeKind.Array, elementType)
    {
        Size = size;
    }

    /// <summary>
    /// Gets the size of the array.
    /// </summary>
    public int Size { get; }

    public override int SizeOf
    {
        get => Size * ElementType.SizeOf;
        set => throw new InvalidOperationException("Cannot set the SizeOf an array type. The SizeOf is calculated by the SizeOf its ElementType and the number of elements in the fixed array");
    }

    public override CppType GetCanonicalType()
    {
        var elementTypeCanonical = ElementType.GetCanonicalType();
        if (ReferenceEquals(elementTypeCanonical, ElementType)) return this;
        return new CppArrayType(Cursor.CanonicalCursor, elementTypeCanonical, Size);
    }

    public override string ToString()
    {
        return $"{ElementType.GetDisplayName()}[{Size}]";
    }
}
