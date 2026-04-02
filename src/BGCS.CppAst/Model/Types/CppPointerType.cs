// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

using ClangSharp.Interop;
using BGCS.CppAst.Extensions;
using System;

namespace BGCS.CppAst.Model.Types;
/// <summary>
/// A C++ pointer type (e.g `int*`)
/// </summary>
public sealed class CppPointerType : CppTypeWithElementType
{
    /// <summary>
    /// Constructor of a pointer type.
    /// </summary>
    /// <param name="cursor"></param>
    /// <param name="elementType">The element type pointed to.</param>
    public CppPointerType(CXCursor cursor, CppType elementType) : base(cursor, CppTypeKind.Pointer, elementType)
    {
        SizeOf = nint.Size;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{ElementType.GetDisplayName()} *";
    }

    /// <inheritdoc />
    public override CppType GetCanonicalType()
    {
        var elementTypeCanonical = ElementType.GetCanonicalType();
        if (ReferenceEquals(elementTypeCanonical, ElementType)) return this;
        return new CppPointerType(Cursor.CanonicalCursor, elementTypeCanonical);
    }
}
