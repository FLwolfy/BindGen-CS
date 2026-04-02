using System;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

namespace BGCS.CppAst.Model.Types;
/// <summary>
/// Defines the type of storage.
/// </summary>
public enum CppStorageQualifier
{
    /// <summary>
    /// No storage defined.
    /// </summary>
    None,
    /// <summary>
    /// Extern storage
    /// </summary>
    Extern,
    /// <summary>
    /// Static storage.
    /// </summary>
    Static,
}
