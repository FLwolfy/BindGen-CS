// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using System;
using BGCS.CppAst.Extensions;
using ClangSharp.Interop;

namespace BGCS.CppAst.Model.Types;

/// <summary>
/// A C++ array (e.g int[5] or int[])
/// </summary>
public sealed class CppArrayType : CppTypeWithElementType
{
    /// <summary>
    /// Captures the borrowed native type and its specialization or array shape.
    /// </summary>
    /// <param name="cursor">
    /// The borrowed Clang cursor, valid only while its owning compilation remains alive; default creates a synthetic node.
    /// </param>
    /// <param name="elementType">
    /// The non-null borrowed native array element type.
    /// </param>
    /// <param name="size">
    /// The fixed element count captured for this array.
    /// </param>
    public CppArrayType(
        CXCursor cursor,
        CppType elementType,
        int size
    ) : base(cursor, CppTypeKind.Array, elementType)
    {
        this.size = size;
    }

    /// <summary>
    /// Gets the size of the array.
    /// </summary>
    public int size { get; }
    /// <summary>
    /// Gets the element-count product of the current element size; setting derived array size is unsupported.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The caller attempts to assign the derived byte size.
    /// </exception>
    public override int sizeOf { get => this.size * this.elementType.sizeOf; set => throw new InvalidOperationException("Cannot set the SizeOf an array type. The SizeOf is calculated by the SizeOf its ElementType and the number of elements in the fixed array"); }

    /// <summary>
    /// Canonicalizes the borrowed array element type while retaining the fixed element count.
    /// </summary>
    /// <returns>
    /// This array when its element is already canonical; otherwise a new borrowed array projection with the same count.
    /// </returns>
    public override CppType GetCanonicalType()
    {
        var elementTypeCanonical = this.elementType.GetCanonicalType();
        if (ReferenceEquals(elementTypeCanonical, this.elementType))
            return this;
        return new CppArrayType(this.cursor.CanonicalCursor, elementTypeCanonical, this.size);
    }

    /// <summary>
    /// Formats the native element display name and fixed array extent.
    /// </summary>
    /// <returns>
    /// The diagnostic native array spelling, unsuitable for persisted binding identity.
    /// </returns>
    public override string ToString()
    {
        return $"{this.elementType.GetDisplayName()}[{this.size}]";
    }
}
