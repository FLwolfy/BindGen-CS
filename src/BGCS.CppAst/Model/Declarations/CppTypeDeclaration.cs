using System.Collections.Generic;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Model.Metadata;
using BGCS.CppAst.Model.Types;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using ClangSharp.Interop;

namespace BGCS.CppAst.Model.Declarations;

/// <summary>
/// Base class for a type declaration (<see cref = "CppClass"/>, <see cref = "CppEnum"/>, <see cref = "CppFunctionType"/> or <see cref = "CppTypedef"/>)
/// </summary>
public abstract class CppTypeDeclaration : CppType, ICppDeclaration, ICppContainer
{
    /// <summary>
    /// Creates an analysis model borrowing native data from its owning compilation.
    /// </summary>
    /// <param name="cursor">Borrowed declaration cursor from a live compilation.</param>
    /// <param name="typeKind">Semantic type category represented by this declaration.</param>
    protected CppTypeDeclaration(
        CXCursor cursor,
        CppTypeKind typeKind
    ) : base(cursor, typeKind)
    {
    }

    /// <inheritdoc/>
    public CppComment? comment { get; set; }
    /// <inheritdoc/>
    public virtual IEnumerable<ICppDeclaration> children => [];
}
