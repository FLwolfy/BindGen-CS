using System;
using System.Collections.Generic;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

using BGCS.CppAst.Collections;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Metadata;

namespace BGCS.CppAst.Model.Interfaces;
/// <summary>
/// A <see cref="ICppContainer"/> that can contain also namespaces.
/// </summary>
/// <seealso cref="CppNamespace"/>
/// <seealso cref="CppCompilation"/>
/// <seealso cref="CppGlobalDeclarationContainer"/>
public interface ICppGlobalDeclarationContainer : ICppDeclarationContainer
{
    /// <summary>
    /// Gets the declared namespaces
    /// </summary>
    CppContainerList<CppNamespace> Namespaces { get; }
}
