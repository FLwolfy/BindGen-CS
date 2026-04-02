// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

using System;
using System.Text;

namespace BGCS.CppAst.Model.Attributes;
/// <summary>
/// Attribute kind enum here
/// </summary>
public enum AttributeKind
{
    CxxSystemAttribute,

    ////CxxCustomAttribute,
    AnnotateAttribute,

    CommentAttribute,
    TokenAttribute,         //the attribute is parse from token, and the parser is slow.
    ObjectiveCAttribute,
}
