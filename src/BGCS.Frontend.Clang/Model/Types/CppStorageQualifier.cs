// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
namespace BGCS.Frontend.Clang.Model.Types;

/// <summary>
/// Defines the type of storage.
/// </summary>
internal enum CppStorageQualifier
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
