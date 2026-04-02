using System;
using System.Collections.Generic;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

using BGCS.CppAst.AttributeUtils;
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
    List<CppAttribute> Attributes { get; }

    [Obsolete("TokenAttributes is deprecated. please use system attributes and annotate attributes")]
    List<CppAttribute> TokenAttributes { get; }

    MetaAttributeMap MetaAttributes { get; }
}
