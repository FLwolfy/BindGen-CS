// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Text;
using BGCS.CppAst.AttributeParsing;
using BGCS.CppAst.Extensions;
using BGCS.CppAst.Model.Attributes;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Model.Types;
using ClangSharp.Interop;

namespace BGCS.CppAst.Model.Declarations;

/// <summary>
/// An Objective-C proeprty.
/// </summary>
public sealed class CppProperty : CppDeclaration, ICppMember, ICppAttributeContainer
{
    /// <summary>
    /// Creates a mutable native property projection borrowing its compilation lifetime.
    /// </summary>
    /// <param name="cursor">
    /// The borrowed Clang cursor, valid only while its owning compilation remains alive; default creates a synthetic node.
    /// </param>
    /// <param name="type">
    /// The non-null borrowed native property type.
    /// </param>
    /// <param name="name">
    /// The native property identifier.
    /// </param>
    public CppProperty(
        CXCursor cursor,
        CppType type,
        string name
    ) : base(cursor)
    {
        this.type = type ?? throw new ArgumentNullException(nameof(type));
        this.name = name;
        this.attributes = [];
        this.getterName = string.Empty;
        this.setterName = string.Empty;
    }

    /// <summary>
    /// Gets attached attributes. Might be null.
    /// </summary>
    public List<CppAttribute> attributes { get; }
    /// <summary>
    /// Gets mutable attributes recovered from source tokens outside the native attribute-cursor list.
    /// </summary>
    public List<CppAttribute> tokenAttributes { get; } = [];
    /// <summary>
    /// Gets the mutable recognized annotation map owned by this declaration.
    /// </summary>
    public MetaAttributeMap metaAttributes { get; private set; } = new MetaAttributeMap();
    /// <summary>
    /// Gets the type of this field/variable.
    /// </summary>
    public CppType type { get; set; }
    /// <inheritdoc/>
    public string name { get; set; }
    /// <summary>
    /// Gets or sets the name of the getter method.
    /// </summary>
    internal string getterName { get; set; }
    /// <summary>
    /// Gets or sets the getter method.
    /// </summary>
    public CppFunction? getter { get; set; }
    /// <summary>
    /// Gets or sets the name of the setter method.
    /// </summary>
    internal string setterName { get; set; }
    /// <summary>
    /// Gets or sets the setter method.
    /// </summary>
    public CppFunction? setter { get; set; }

    /// <inheritdoc/>
    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.Append(this.type.GetDisplayName());
        builder.Append(' ');
        builder.Append(this.name);
        return builder.ToString();
    }
}
