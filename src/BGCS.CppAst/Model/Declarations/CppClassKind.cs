using System;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

namespace BGCS.CppAst.Model.Declarations;
/// <summary>
/// Type of a <see cref="CppClass"/> (class, struct or union)
/// </summary>
public enum CppClassKind
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
    /// <summary>
    /// An Objective-C `@interface`
    /// </summary>
    ObjCInterface,
    /// <summary>
    /// An Objective-C `@protocol`
    /// </summary>
    ObjCProtocol,
    /// <summary>
    /// An Objective-C `@interface` with a category.
    /// </summary>
    ObjCInterfaceCategory,
}
