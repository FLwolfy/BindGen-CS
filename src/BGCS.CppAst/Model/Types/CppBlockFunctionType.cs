// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.


using ClangSharp.Interop;
using BGCS.CppAst.Model.Declarations;
using System;
using System.Collections.Generic;
using System.Text;

namespace BGCS.CppAst.Model.Types;
/// <summary>
/// An Objective-C block function type (e.g `void (^)(int arg1, int arg2)`)
/// </summary>
public sealed class CppBlockFunctionType : CppFunctionTypeBase
{
    /// <summary>
    /// Constructor of a function type.
    /// </summary>
    /// <param name="cursor"></param>
    /// <param name="returnType">Return type of this function type.</param>
    public CppBlockFunctionType(CXCursor cursor, CppType returnType) : base(cursor, CppTypeKind.ObjCBlockFunction, returnType)
    {
    }
}
