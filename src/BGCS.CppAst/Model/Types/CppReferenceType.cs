// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

using ClangSharp.Interop;
using BGCS.CppAst.Extensions;
using System;

namespace BGCS.CppAst.Model.Types;
/// <summary>
/// A C++ reference type (e.g `int&amp;`)
/// </summary>
public sealed class CppReferenceType : CppTypeWithElementType
{
    /// <summary>
    /// Constructor of a reference type.
    /// </summary>
    /// <param name="cursor"></param>
    /// <param name="elementType">The element type referenced to.</param>
    public CppReferenceType(CXCursor cursor, CppType elementType) : base(cursor, CppTypeKind.Reference, elementType)
    {
    }

    /// <inheritdoc />
    public override int SizeOf
    {
        get => ElementType.SizeOf;
        set => throw new InvalidOperationException("Cannot override the SizeOf of a Reference type");
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{ElementType.GetDisplayName()}&";
    }

    /// <inheritdoc />
    public override CppType GetCanonicalType()
    {
        var elementTypeCanonical = ElementType.GetCanonicalType();
        return ReferenceEquals(elementTypeCanonical, ElementType) ? this : new CppReferenceType(Cursor.CanonicalCursor, elementTypeCanonical);
    }
}
