// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using BGCS.CppAst.Model.Declarations;
using ClangSharp.Interop;

namespace BGCS.CppAst.Model.Types;

/// <summary>
/// An Objective-C block function type (e.g `void (^)(int arg1, int arg2)`)
/// </summary>
public sealed class CppBlockFunctionType : CppFunctionTypeBase
{
    /// <summary>
    /// Captures a borrowed native type relationship for attempt-local analysis.
    /// </summary>
    /// <param name="cursor">
    /// The borrowed Clang cursor, valid only while its compilation remains alive; default creates a synthetic node.
    /// </param>
    /// <param name="returnType">
    /// The non-null borrowed native result type.
    /// </param>
    public CppBlockFunctionType(
        CXCursor cursor,
        CppType returnType
    ) : base(cursor, CppTypeKind.ObjCBlockFunction, returnType)
    {
    }
}
