using System;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

namespace BGCS.CppAst.Model.Templates;
/// <summary>
/// Type of a template
/// </summary>
public enum CppTemplateKind
{
	/// <summary>
	/// not a template class, just a normal class
	/// </summary>
	NormalClass,
	/// <summary>
	/// A class template
	/// </summary>
	TemplateClass,
	/// <summary>
	/// A partial template class
	/// </summary>
	PartialTemplateClass,
	/// <summary>
	/// A class with full template specialized
	/// </summary>
	TemplateSpecializedClass,
	/// <summary>
	/// An Objective-C class template
	/// </summary>
	ObjCGenericClass,
}

