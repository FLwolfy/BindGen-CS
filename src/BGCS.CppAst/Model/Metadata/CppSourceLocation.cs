using System;
using System.IO;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

namespace BGCS.CppAst.Model.Metadata;
/// <summary>
/// Defines a location within a source file.
/// </summary>
public struct CppSourceLocation
{
    /// <summary>
    /// Constructor of a location within a source file.
    /// </summary>
    /// <param name="file">The file path</param>
    /// <param name="offset">The char offset from the beginning of the file.</param>
    /// <param name="line">The line (starting from 1)</param>
    /// <param name="column">The column (starting from 1)</param>
    public CppSourceLocation(string file, int offset, int line, int column)
    {
        File = file;
        Offset = offset;
        Line = line;
        Column = column;
    }

    /// <summary>
    /// Gets or sets the file associated with this location.
    /// </summary>
    public readonly string File;

    /// <summary>
    /// Gets or sets the char offset from the beginning of the file.
    /// </summary>
    public readonly int Offset;

    /// <summary>
    /// Gets or sets the line (start from 1) of this location.
    /// </summary>
    public readonly int Line;

    /// <summary>
    /// Gets or sets the column (start from 1) of this location.
    /// </summary>
    public readonly int Column;

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{File}({Line}, {Column})";
    }
}
