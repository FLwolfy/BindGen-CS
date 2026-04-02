using System;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

namespace BGCS.CppAst.Model.Interfaces;
using ClangSharp.Interop;

/// <summary>
/// Base interface of for <see cref="CppElement"/>
/// </summary>
public interface ICppElement
{
    public CXCursor Cursor { get; }
}
