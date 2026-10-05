// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using System;
using BGCS.CppAst.Extensions;
using ClangSharp.Interop;

namespace BGCS.CppAst.Model.Types;

/// <summary>
/// A C++ reference type (e.g `int&amp;`)
/// </summary>
public sealed class CppReferenceType : CppTypeWithElementType
{
    /// <summary>
    /// Captures a borrowed native type relationship for attempt-local analysis.
    /// </summary>
    /// <param name="cursor">
    /// The borrowed Clang cursor, valid only while its compilation remains alive; default creates a synthetic node.
    /// </param>
    /// <param name="elementType">
    /// The non-null borrowed native referent type.
    /// </param>
    public CppReferenceType(
        CXCursor cursor,
        CppType elementType
    ) : base(cursor, CppTypeKind.Reference, elementType)
    {
    }

    /// <inheritdoc/>
    public override int sizeOf { get => this.elementType.sizeOf; set => throw new InvalidOperationException("Cannot override the SizeOf of a Reference type"); }

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"{this.elementType.GetDisplayName()}&";
    }

    /// <inheritdoc/>
    public override CppType GetCanonicalType()
    {
        var elementTypeCanonical = this.elementType.GetCanonicalType();
        return ReferenceEquals(elementTypeCanonical, this.elementType) ? this : new CppReferenceType(this.cursor.CanonicalCursor, elementTypeCanonical);
    }
}
