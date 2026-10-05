using System.Collections.Generic;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using BGCS.CppAst.AttributeParsing;
using BGCS.CppAst.Model.Attributes;

namespace BGCS.CppAst.Model.Interfaces;

/// <summary>
/// Base interface for all with attribute element.
/// </summary>
public interface ICppAttributeContainer
{
    /// <summary>
    /// Gets the attributes from element.
    /// </summary>
    List<CppAttribute> attributes { get; }

    /// <summary>
    /// Attributes recovered from declaration tokens when native AST attributes are incomplete.
    /// </summary>
    List<CppAttribute> tokenAttributes { get; }

    /// <summary>
    /// Semantic attribute values interpreted from this declaration.
    /// </summary>
    MetaAttributeMap metaAttributes { get; }
}
