using System;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BGCS.CppAst.Model.Interfaces;
/// <summary>
/// Base tag interface used to describe a container of <see cref="CppElement"/>
/// </summary>
public interface ICppContainer
{
    /// <summary>
    /// Gets of declaration from this container.
    /// </summary>
    /// <returns>A list of Cpp declaration</returns>
    IEnumerable<ICppDeclaration> Children { get; }
}
