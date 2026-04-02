using System;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

namespace BGCS.CppAst.Model.Types;
/// <summary>
/// Qualifiers for a <see cref="CppQualifiedType"/>
/// </summary>
public enum CppTypeQualifier
{
    /// <summary>
    /// The type is `const`
    /// </summary>
    Const,

    /// <summary>
    /// The type is `volatile`
    /// </summary>
    Volatile,
}
