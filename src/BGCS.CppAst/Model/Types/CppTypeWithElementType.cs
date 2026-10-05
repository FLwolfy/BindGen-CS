// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using System;
using ClangSharp.Interop;

namespace BGCS.CppAst.Model.Types;

/// <summary>
/// Base class for a type using an element type.
/// </summary>
public abstract class CppTypeWithElementType : CppType
{
    /// <summary>
    /// Creates an analysis model borrowing native data from its owning compilation.
    /// </summary>
    /// <param name="cursor">Borrowed type cursor from a live compilation.</param>
    /// <param name="typeKind">Compound type category.</param>
    /// <param name="elementType">Type referenced or contained by this compound type.</param>
    protected CppTypeWithElementType(
        CXCursor cursor,
        CppTypeKind typeKind,
        CppType elementType
    ) : base(cursor, typeKind)
    {
        this.elementType = elementType ?? throw new ArgumentNullException(nameof(elementType));
    }

    /// <summary>
    /// Gets the borrowed native type contained or referenced by this compound type.
    /// </summary>
    public CppType elementType { get; }
    /// <inheritdoc/>
    public override int sizeOf { get; set; }
}
