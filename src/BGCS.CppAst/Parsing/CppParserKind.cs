using System;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

namespace BGCS.CppAst.Parsing;

/// <summary>
/// Defines values for <c>CppParserKind</c>.
/// </summary>
public enum CppParserKind
{
    /// <summary>
    /// No parser kind defined.
    /// </summary>
    None = 0,

    /// <summary>
    /// Equivalent to -xc++. (Default)
    /// </summary>
    Cpp,

    /// <summary>
    /// Equivalent to -xc.
    /// </summary>
    C,

    /// <summary>
    /// Equivalent to -xobjective-c.
    /// </summary>
    ObjC
}
