using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Model.Metadata;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using ClangSharp.Interop;

namespace BGCS.CppAst.Model.Declarations;

/// <summary>
/// Base class for any declaration that is not a type (<see cref = "CppTypeDeclaration"/>)
/// </summary>
public abstract class CppDeclaration : CppElement, ICppDeclaration
{
    /// <summary>
    /// Creates an analysis model borrowing native data from its owning compilation.
    /// </summary>
    /// <param name="cursor">Borrowed declaration cursor from a live compilation.</param>
    protected CppDeclaration(CXCursor cursor) : base(cursor)
    {
    }

    /// <summary>
    /// Gets or sets the comment attached to this element. Might be null.
    /// </summary>
    public CppComment? comment { get; set; }
}
