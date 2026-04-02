// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.


using ClangSharp.Interop;
using BGCS.CppAst.AttributeUtils;
using BGCS.CppAst.Extensions;
using BGCS.CppAst.Model.Attributes;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Model.Types;
using System;
using System.Collections.Generic;
using System.Text;

namespace BGCS.CppAst.Model.Declarations;

/// <summary>
/// An Objective-C proeprty.
/// </summary>
public sealed class CppProperty : CppDeclaration, ICppMember, ICppAttributeContainer
{
    public CppProperty(CXCursor cursor, CppType type, string name) : base(cursor)
    {
        Type = type ?? throw new ArgumentNullException(nameof(type));
        Name = name;
        Attributes = [];
    }

    /// <summary>
    /// Gets attached attributes. Might be null.
    /// </summary>
    public List<CppAttribute> Attributes { get; }

    [Obsolete("TokenAttributes is deprecated. please use system attributes and annotate attributes")]
    public List<CppAttribute> TokenAttributes { get; } = [];

    public MetaAttributeMap MetaAttributes { get; private set; } = new MetaAttributeMap();

    /// <summary>
    /// Gets the type of this field/variable.
    /// </summary>
    public CppType Type { get; set; }

    /// <inheritdoc />
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the name of the getter method.
    /// </summary>
    internal string GetterName { get; set; }

    /// <summary>
    /// Gets or sets the getter method.
    /// </summary>
    public CppFunction? Getter { get; set; }

    /// <summary>
    /// Gets or sets the name of the setter method.
    /// </summary>
    internal string SetterName { get; set; }

    /// <summary>
    /// Gets or sets the setter method.
    /// </summary>
    public CppFunction? Setter { get; set; }

    /// <inheritdoc />
    public override string ToString()
    {
        var builder = new StringBuilder();

        builder.Append(Type.GetDisplayName());
        builder.Append(' ');
        builder.Append(Name);
        return builder.ToString();
    }
}