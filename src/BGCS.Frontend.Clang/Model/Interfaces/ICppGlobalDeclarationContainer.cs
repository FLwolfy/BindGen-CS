// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using BGCS.Frontend.Clang.Collections;
using BGCS.Frontend.Clang.Model.Declarations;
using BGCS.Frontend.Clang.Model.Metadata;

namespace BGCS.Frontend.Clang.Model.Interfaces;

/// <summary>
/// A <see cref = "ICppContainer"/> that can contain also namespaces.
/// </summary>
/// <seealso cref = "CppNamespace"/>
/// <seealso cref = "CppCompilation"/>
/// <seealso cref = "CppGlobalDeclarationContainer"/>
internal interface ICppGlobalDeclarationContainer : ICppDeclarationContainer
{
    /// <summary>
    /// Gets the declared namespaces
    /// </summary>
    CppContainerList<CppNamespace> namespaces { get; }
}
