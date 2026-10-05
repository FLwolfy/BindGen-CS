// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using BGCS.CppAst.Model.Types;
using ClangSharp.Interop;

namespace BGCS.CppAst.Model.Declarations;

/// <summary>
/// A C++ function type (e.g `void (*)(int arg1, int arg2)`)
/// </summary>
public sealed class CppFunctionType : CppFunctionTypeBase
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
    public CppFunctionType(
        CXCursor cursor,
        CppType returnType
    ) : base(cursor, CppTypeKind.Function, returnType)
    {
    }
}
