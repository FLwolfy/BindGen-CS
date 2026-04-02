using System;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

namespace BGCS.CppAst.Model.Templates;
/// <summary>
/// Type of a template argument
/// </summary>
public enum CppTemplateArgumentKind
{
    AsType,
    AsInteger,
    Unknown,
}

