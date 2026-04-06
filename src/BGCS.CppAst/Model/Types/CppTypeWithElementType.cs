// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

using ClangSharp.Interop;
using System;

namespace BGCS.CppAst.Model.Types;
/// <summary>
/// Base class for a type using an element type.
/// </summary>
public abstract class CppTypeWithElementType : CppType
{
    protected CppTypeWithElementType(CXCursor cursor, CppTypeKind typeKind, CppType elementType) : base(cursor, typeKind)
    {
        ElementType = elementType ?? throw new ArgumentNullException(nameof(elementType));
    }

    /// <summary>
    /// Gets <c>ElementType</c>.
    /// </summary>
    public CppType ElementType { get; }

    /// <inheritdoc />
    public override int SizeOf { get; set; }
}
