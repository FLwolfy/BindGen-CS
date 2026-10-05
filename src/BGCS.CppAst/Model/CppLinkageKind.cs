// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
namespace BGCS.CppAst.Model;

/// <summary>
/// Type of linkage.
/// </summary>
public enum CppLinkageKind
{
    /// <summary>
    /// No valid linkage information is available.
    /// </summary>
    Invalid,
    /// <summary>
    /// The declaration does not participate in linkage.
    /// </summary>
    NoLinkage,
    /// <summary>
    /// The declaration is visible only within its translation unit.
    /// </summary>
    Internal,
    /// <summary>
    /// The declaration has a unique externally linked identity.
    /// </summary>
    UniqueExternal,
    /// <summary>
    /// The declaration can be linked across translation units.
    /// </summary>
    External,
}
