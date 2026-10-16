// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
namespace BGCS.Frontend.Clang.Model.Interfaces;

using BGCS.Frontend.Clang.Model.Metadata;

/// <summary>
/// Base interface for all Cpp declaration.
/// </summary>
internal interface ICppDeclaration : ICppElement
{
    /// <summary>
    /// Gets or sets the comment attached to this element. Might be null.
    /// </summary>
    CppComment? comment { get; set; }
}
