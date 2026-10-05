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
    /// Creates a mutable native include directive projection borrowing its compilation lifetime.
    /// </summary>
    /// <param name="cursor">
    /// The borrowed Clang cursor, valid only while its owning compilation remains alive; default creates a synthetic node.
    /// </param>
    /// <param name="fileName">
    /// The native included-file spelling retained by the projection.
    /// </param>
    public CppInclusionDirective(
        CXCursor cursor,
        string fileName
    ) : base(cursor)
    {
        this.fileName = fileName;
    }

    /// <summary>
    /// Gets or sets the file name being included.
    /// </summary>
    public string fileName { get; set; }

    /// <inheritdoc/>
    public override string ToString() => this.fileName ?? "<empty>";
}
