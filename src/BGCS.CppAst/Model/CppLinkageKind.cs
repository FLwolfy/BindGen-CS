using System;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

namespace BGCS.CppAst.Model;
/// <summary>
/// Type of linkage.
/// </summary>
public enum CppLinkageKind
{
    Invalid,
    NoLinkage,
    Internal,
    UniqueExternal,
    External,
}
