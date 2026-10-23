// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
namespace BGCS.Frontend.Clang.Model.Interfaces;

using ClangSharp.Interop;

/// <summary>
/// Exposes the borrowed native cursor shared by parser model elements.
/// </summary>
internal interface ICppElement
{
    /// <summary>
    /// Gets the borrowed native cursor; accessing native data requires its owning compilation to remain alive.
    /// </summary>
    public CXCursor cursor { get; }
}
