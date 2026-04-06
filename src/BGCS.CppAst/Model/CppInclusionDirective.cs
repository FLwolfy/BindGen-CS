using System;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

namespace BGCS.CppAst.Model;

using ClangSharp.Interop;

/// <summary>
/// Represents a header inclusion directive.
/// </summary>
public class CppInclusionDirective : CppElement
{
    /// <summary>
    /// Initializes a new instance of <see cref="CppInclusionDirective"/>.
    /// </summary>
    public CppInclusionDirective(CXCursor cursor, string fileName) : base(cursor)
    {
        FileName = fileName;
    }

    /// <summary>
    /// Gets or sets the file name being included.
    /// </summary>
    public string FileName { get; set; }

    /// <inheritdoc />
    public override string ToString() => FileName ?? "<empty>";
}
