using System;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

using ClangSharp.Interop;
using BGCS.CppAst.Extensions;

namespace BGCS.CppAst.Model.Types;
/// <summary>
/// A C++ qualified type (e.g `const int`)
/// </summary>
public sealed class CppQualifiedType : CppTypeWithElementType
{
    /// <summary>
    /// Constructor for a C++ qualified type.
    /// </summary>
    /// <param name="cursor"></param>
    /// <param name="qualifier">The C++ qualified (e.g `const`)</param>
    /// <param name="elementType">The element type (e.g `int`)</param>
    public CppQualifiedType(CXCursor cursor, CppTypeQualifier qualifier, CppType elementType) : base(cursor, CppTypeKind.Qualified, elementType)
    {
        Qualifier = qualifier;
        SizeOf = elementType.SizeOf;
    }

    /// <summary>
    /// Gets the qualifier
    /// </summary>
    public CppTypeQualifier Qualifier { get; }

    /// <inheritdoc />
    public override CppType GetCanonicalType()
    {
        var elementTypeCanonical = ElementType.GetCanonicalType();
        return ReferenceEquals(elementTypeCanonical, ElementType) ? this : new CppQualifiedType(Cursor.CanonicalCursor, Qualifier, elementTypeCanonical);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{ElementType.GetDisplayName()} {Qualifier.ToString().ToLowerInvariant()}";
    }
}
