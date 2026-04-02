using System;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

namespace BGCS.CppAst.Model.Types;
/// <summary>
/// Kinds of a C++ type (e.g primitive, pointer...)
/// </summary>
public enum CppTypeKind
{
    /// <summary>
    /// A primitive type (e.g `int`, `short`, `double`...)
    /// </summary>
    Primitive,
    /// <summary>
    /// A Pointer type (e.g `int*`)
    /// </summary>
    Pointer,
    /// <summary>
    /// A reference type (e.g `int&amp;`)
    /// </summary>
    Reference,
    /// <summary>
    /// An array type (e.g int[5])
    /// </summary>
    Array,
    /// <summary>
    /// A qualified type (e.g const int)
    /// </summary>
    Qualified,
    /// <summary>
    /// A function type
    /// </summary>
    Function,
    /// <summary>
    /// A typedef
    /// </summary>
    Typedef,
    /// <summary>
    /// A struct or a class.
    /// </summary>
    StructOrClass,
    /// <summary>
    /// An standard or scoped enum
    /// </summary>
    Enum,
    /// <summary>
    /// A template parameter type.
    /// </summary>
    TemplateParameterType,
	/// <summary>
	/// A none type template parameter type.
	/// </summary>
	TemplateParameterNonType,
	/// <summary>
	/// A template specialized argument type.
	/// </summary>
	TemplateArgumentType,
	/// <summary>
	/// An unexposed type.
	/// </summary>
	Unexposed,
	/// <summary>
	/// An Objective-C block function type.
	/// </summary>
	ObjCBlockFunction,
	/// <summary>
	/// A generic type (e.g. Objective-C `MyType&lt;TArg&gt;`)
	/// </summary>
	GenericType,
    /// <summary>
    /// An Objective-C interface with a category.
    /// </summary>
    ObjCInterfaceWithCategory,
}
