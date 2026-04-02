using System;
using System.Collections.Generic;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

using ClangSharp.Interop;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Model.Metadata;
using BGCS.CppAst.Model.Types;

namespace BGCS.CppAst.Model.Declarations;
/// <summary>
/// Base class for a type declaration (<see cref="CppClass"/>, <see cref="CppEnum"/>, <see cref="CppFunctionType"/> or <see cref="CppTypedef"/>)
/// </summary>
public abstract class CppTypeDeclaration : CppType, ICppDeclaration, ICppContainer
{
    protected CppTypeDeclaration(CXCursor cursor, CppTypeKind typeKind) : base(cursor, typeKind)
    {
    }

    /// <inheritdoc />
    public CppComment? Comment { get; set; }

    /// <inheritdoc />
    public virtual IEnumerable<ICppDeclaration> Children => [];
}
