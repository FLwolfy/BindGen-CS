using System;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

namespace BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Model.Metadata;

/// <summary>
/// Base interface for all Cpp declaration.
/// </summary>
public interface ICppDeclaration : ICppElement
{
    /// <summary>
    /// Gets or sets the comment attached to this element. Might be null.
    /// </summary>
    CppComment? Comment { get; set; }
}
