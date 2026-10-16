// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using System;
using BGCS.Frontend.Clang.Extensions;
using ClangSharp.Interop;

namespace BGCS.Frontend.Clang.Model.Types;

/// <summary>
/// A C++ reference type (e.g `int&amp;`)
/// </summary>
internal sealed class CppReferenceType : CppTypeWithElementType
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
    /// <param name="isRValue">Whether the source reference preserves rvalue value category.</param>
    public CppReferenceType(
        CXCursor cursor,
        CppType elementType,
        bool isRValue = false
    ) : base(cursor, CppTypeKind.Reference, elementType)
    {
        this.isRValue = isRValue;
    }

    /// <summary>Gets whether this reference has native rvalue value category.</summary>
    public bool isRValue { get; }

    /// <inheritdoc/>
    public override int sizeOf { get => this.elementType.sizeOf; set => throw new InvalidOperationException("Cannot override the SizeOf of a Reference type"); }

    /// <inheritdoc/>
    public override string ToString()
    {
        return this.elementType.GetDisplayName() + (this.isRValue ? "&&" : "&");
    }

    /// <inheritdoc/>
    public override CppType GetCanonicalType()
    {
        var elementTypeCanonical = this.elementType.GetCanonicalType();
        return ReferenceEquals(elementTypeCanonical, this.elementType) ? this : new CppReferenceType(this.cursor.CanonicalCursor, elementTypeCanonical, this.isRValue);
    }
}
