using System;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

namespace BGCS.CppAst.Model.Interfaces;
/// <summary>
/// Interface of a <see cref="ICppMember"/> with a <see cref="CppVisibility"/>.
/// </summary>
public interface ICppMemberWithVisibility : ICppMember
{
    /// <summary>
    /// Gets or sets the visibility of this element.
    /// </summary>
    CppVisibility Visibility { get; set; }
}
