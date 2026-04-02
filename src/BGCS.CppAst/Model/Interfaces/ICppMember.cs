using System;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

namespace BGCS.CppAst.Model.Interfaces;
/// <summary>
/// A C++ declaration that has a name
/// </summary>
public interface ICppMember : ICppElement
{
    /// <summary>
    /// Name of this C++ declaration.
    /// </summary>
    string Name { get; set; }
}
