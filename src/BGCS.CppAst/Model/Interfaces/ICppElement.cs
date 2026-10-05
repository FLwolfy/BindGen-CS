// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
namespace BGCS.CppAst.Model.Interfaces;

using ClangSharp.Interop;

/// <summary>
/// Exposes the borrowed native cursor shared by parser model elements.
/// </summary>
public interface ICppElement
{
    /// <summary>
    /// Gets the borrowed native cursor; accessing native data requires its owning compilation to remain alive.
    /// </summary>
    public CXCursor cursor { get; }
}
