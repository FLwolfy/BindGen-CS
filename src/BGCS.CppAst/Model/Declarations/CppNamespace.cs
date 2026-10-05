// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using System;
using System.Collections.Generic;
using BGCS.CppAst.AttributeParsing;
using BGCS.CppAst.Collections;
using BGCS.CppAst.Model.Attributes;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Utilities;
using ClangSharp.Interop;

namespace BGCS.CppAst.Model.Declarations;

/// <summary>
/// Defines a C++ namespace. This is only one level of namespace (e.g `A` in `A::B::C`)
/// </summary>
public class CppNamespace : CppDeclaration, ICppMember, ICppGlobalDeclarationContainer
{
    /// <summary>
    /// Creates a mutable native namespace projection with empty owned child collections.
    /// </summary>
    /// <param name="cursor">
    /// The borrowed Clang cursor, valid only while its owning compilation remains alive; default creates a synthetic node.
    /// </param>
    /// <param name="name">
    /// The native declaration identifier, empty for an unnamed declaration.
    /// </param>
    public CppNamespace(
        CXCursor cursor,
        string name
    ) : base(cursor)
    {
        this.name = name ?? throw new ArgumentNullException(nameof(name));
        this.fields = new CppContainerList<CppField>(this);
        this.functions = new CppContainerList<CppFunction>(this);
        this.enums = new CppContainerList<CppEnum>(this);
        this.classes = new CppContainerList<CppClass>(this);
        this.typedefs = new CppContainerList<CppTypedef>(this);
        this.namespaces = new CppContainerList<CppNamespace>(this);
        this.attributes = [];
        this.properties = new CppContainerList<CppProperty>(this);
    }

    /// <summary>
    /// Name of the namespace.
    /// </summary>
    public string name { get; set; }
    /// <summary>
    /// Is the namespace inline or not(such as std::__1::vector).
    /// </summary>
    public bool isInlineNamespace { get; set; }
    /// <inheritdoc/>
    public CppContainerList<CppField> fields { get; }
    /// <inheritdoc/>
    public CppContainerList<CppProperty> properties { get; }
    /// <inheritdoc/>
    public CppContainerList<CppFunction> functions { get; }
    /// <inheritdoc/>
    public CppContainerList<CppEnum> enums { get; }
    /// <inheritdoc/>
    public CppContainerList<CppClass> classes { get; }
    /// <inheritdoc/>
    public CppContainerList<CppTypedef> typedefs { get; }
    /// <inheritdoc/>
    public CppContainerList<CppNamespace> namespaces { get; }
    /// <inheritdoc/>
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
    /// Formats this native namespace identifier as a diagnostic declaration.
    /// </summary>
    /// <returns>
    /// The current diagnostic declaration spelling, not a persisted binding identity.
    /// </returns>
    public override string ToString()
    {
        return $"namespace {this.name} {{...}}";
    }

    /// <summary>
    /// Enumerates borrowed current namespace declarations in the container's category and collection order.
    /// </summary>
    public IEnumerable<ICppDeclaration> children => CppContainerHelper.Children(this);
}
