using System;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

namespace BGCS.CppAst.Model;
/// <summary>
/// Gets the visibility of a C++ element.
/// </summary>
public enum CppVisibility
{
    /// <summary>
    /// Default visibility is undefined or not relevant.
    /// </summary>
    Default,

    /// <summary>
    /// `public` visibility
    /// </summary>
    Public,

    /// <summary>
    /// `protected` visibility
    /// </summary>
    Protected,

    /// <summary>
    /// `private` visibility
    /// </summary>
    Private,
}
