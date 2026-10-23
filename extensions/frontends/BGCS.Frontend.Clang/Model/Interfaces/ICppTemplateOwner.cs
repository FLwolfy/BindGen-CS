// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using BGCS.Frontend.Clang.Collections;
using BGCS.Frontend.Clang.Model.Types;

namespace BGCS.Frontend.Clang.Model.Interfaces;

/// <summary>
/// Base interface of a type/method declared with template parameters.
/// </summary>
internal interface ICppTemplateOwner
{
    /// <summary>
    /// List of template parameters.
    /// </summary>
    CppContainerList<CppType> templateParameters { get; }
}
