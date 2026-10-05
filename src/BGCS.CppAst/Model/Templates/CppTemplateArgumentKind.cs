// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
namespace BGCS.CppAst.Model.Templates;

/// <summary>
/// Type of a template argument
/// </summary>
public enum CppTemplateArgumentKind
{
    /// <summary>
    /// A native type supplied to a template.
    /// </summary>
    AsType,
    /// <summary>
    /// An integral value supplied to a template.
    /// </summary>
    AsInteger,
    /// <summary>
    /// A template argument not represented by the supported model.
    /// </summary>
    Unknown,
}
