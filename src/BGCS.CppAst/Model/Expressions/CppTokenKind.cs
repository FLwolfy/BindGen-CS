using System;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

namespace BGCS.CppAst.Model.Expressions;
/// <summary>
/// Kind of a <see cref="CppToken"/> used by <see cref="CppMacro"/>
/// </summary>
public enum CppTokenKind
{
    /// <summary>
    /// A punctuation token (e.g `=`)
    /// </summary>
    Punctuation,

    /// <summary>
    /// A keyword token (e.g `for`)
    /// </summary>
    Keyword,

    /// <summary>
    /// An identifier token (e.g `my_variable`)
    /// </summary>
    Identifier,

    /// <summary>
    /// A literal token (e.g `15` or `"my string"`)
    /// </summary>
    Literal,

    /// <summary>
    /// A comment token
    /// </summary>
    Comment,
}
