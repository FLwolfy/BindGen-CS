// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
namespace BGCS.Frontend.Clang.Model.Declarations;

/// <summary>
/// Type of a <see cref = "CppClass"/> (class, struct or union)
/// </summary>
internal enum CppClassKind
{
    /// <summary>
    /// A C++ `class`
    /// </summary>
    Class,
    /// <summary>
    /// A C++ `struct`
    /// </summary>
    Struct,
    /// <summary>
    /// A C++ `union`
    /// </summary>
    Union,
}
