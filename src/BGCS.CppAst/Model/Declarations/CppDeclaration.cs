using System;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

using ClangSharp.Interop;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Model.Metadata;

namespace BGCS.CppAst.Model.Declarations;
/// <summary>
/// Base class for any declaration that is not a type (<see cref="CppTypeDeclaration"/>)
/// </summary>
public abstract class CppDeclaration : CppElement, ICppDeclaration
{
    protected CppDeclaration(CXCursor cursor) : base(cursor)
    {
    }

    /// <summary>
    /// Gets or sets the comment attached to this element. Might be null.
    /// </summary>
    public CppComment? Comment { get; set; }
}
