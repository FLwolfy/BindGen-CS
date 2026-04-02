using System;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

using BGCS.CppAst.Collections;
using BGCS.CppAst.Model.Types;
using System.Collections.Generic;

namespace BGCS.CppAst.Model.Interfaces;
/// <summary>
/// Base interface of a type/method declared with template parameters.
/// </summary>
public interface ICppTemplateOwner
{
    /// <summary>
    /// List of template parameters.
    /// </summary>
    CppContainerList<CppType> TemplateParameters { get; }
}
