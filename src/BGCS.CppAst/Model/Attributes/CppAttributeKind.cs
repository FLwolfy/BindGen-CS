// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
namespace BGCS.CppAst.Model.Attributes;

/// <summary>
/// Attribute kind enum here
/// </summary>
public enum AttributeKind
{
    /// <summary>
    /// A C++ attribute recognized by the native parser.
    /// </summary>
    CxxSystemAttribute,
    ////CxxCustomAttribute,
    /// <summary>
    /// A Clang annotation attribute.
    /// </summary>
    AnnotateAttribute,
    /// <summary>
    /// An attribute extracted from structured documentation text.
    /// </summary>
    CommentAttribute,
    /// <summary>
    /// An attribute reconstructed from source tokens.
    /// </summary>
    TokenAttribute, //the attribute is parse from token, and the parser is slow.
    /// <summary>
    /// An Objective-C declaration attribute.
    /// </summary>
    ObjectiveCAttribute,
}
