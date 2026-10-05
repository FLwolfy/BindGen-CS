// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using System;
using BGCS.CppAst.Extensions;
using ClangSharp.Interop;

namespace BGCS.CppAst.Model.Types;

/// <summary>
/// A C++ pointer type (e.g `int*`)
/// </summary>
public sealed class CppPointerType : CppTypeWithElementType
{
    /// <summary>
    /// Captures a borrowed native type relationship for attempt-local analysis.
    /// </summary>
    /// <param name="cursor">
    /// The borrowed Clang cursor, valid only while its compilation remains alive; default creates a synthetic node.
    /// </param>
    /// <param name="elementType">
    /// The non-null borrowed native pointee type.
    /// </param>
    /// <param name="pointerSize">
    /// The native target's positive pointer width in bytes, independent of the generator process.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The target pointer size is not positive.
    /// </exception>
    public CppPointerType(
        CXCursor cursor,
        CppType elementType,
        int pointerSize
    ) : base(cursor, CppTypeKind.Pointer, elementType)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pointerSize);
        this.sizeOf = pointerSize;
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"{this.elementType.GetDisplayName()} *";
    }

    /// <inheritdoc/>
    public override CppType GetCanonicalType()
    {
        var elementTypeCanonical = this.elementType.GetCanonicalType();
        if (ReferenceEquals(elementTypeCanonical, this.elementType))
            return this;
        return new CppPointerType(this.cursor.CanonicalCursor, elementTypeCanonical, this.sizeOf);
    }
}
