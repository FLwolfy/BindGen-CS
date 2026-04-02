// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

using ClangSharp.Interop;
using BGCS.CppAst.Model.Types;
using System;
using System.Collections.Generic;
using System.Text;

namespace BGCS.CppAst.Model.Declarations;
/// <summary>
/// A C++ function type (e.g `void (*)(int arg1, int arg2)`)
/// </summary>
public sealed class CppFunctionType : CppFunctionTypeBase
{
    /// <summary>
    /// Constructor of a function type.
    /// </summary>
    /// <param name="cursor"></param>
    /// <param name="returnType">Return type of this function type.</param>
    public CppFunctionType(CXCursor cursor, CppType returnType) : base(cursor, CppTypeKind.Function, returnType)
    {
    }
}
