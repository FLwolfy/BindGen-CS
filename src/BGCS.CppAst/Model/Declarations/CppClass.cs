using System;
using System.Collections.Generic;
using System.Text;
using BGCS.CppAst.AttributeParsing;
using BGCS.CppAst.Collections;
using BGCS.CppAst.Model.Attributes;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Model.Templates;
using BGCS.CppAst.Model.Types;
using BGCS.CppAst.Utilities;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using ClangSharp.Interop;

namespace BGCS.CppAst.Model.Declarations;

/// <summary>
/// A C++ class, struct or union.
/// </summary>
public class CppClass : CppTypeDeclaration, ICppMemberWithVisibility, ICppDeclarationContainer, ICppTemplateOwner
{
    /// <summary>
    /// Creates a mutable native record projection with empty owned child collections.
    /// </summary>
    /// <param name="cursor">
    /// The borrowed Clang cursor, valid only while its owning compilation remains alive; default creates a synthetic node.
    /// </param>
    /// <param name="name">
    /// The native declaration identifier, empty for an unnamed declaration.
    /// </param>
    public CppClass(
        CXCursor cursor,
        string name
    ) : base(cursor, CppTypeKind.StructOrClass)
    {
        this.name = name ?? throw new ArgumentNullException(nameof(name));
        this.baseTypes = [];
        this.fields = new CppContainerList<CppField>(this);
        this.constructors = new CppContainerList<CppFunction>(this);
        this.destructors = new CppContainerList<CppFunction>(this);
        this.functions = new CppContainerList<CppFunction>(this);
        this.enums = new CppContainerList<CppEnum>(this);
        this.classes = new CppContainerList<CppClass>(this);
        this.typedefs = new CppContainerList<CppTypedef>(this);
        this.templateParameters = new CppContainerList<CppType>(this);
        this.attributes = [];
        this.objCImplementedProtocols = [];
        this.properties = new CppContainerList<CppProperty>(this);
        this.objCCategories = [];
        this.objCCategoryName = string.Empty;
    }

    /// <summary>
    /// Kind of the instance (`class` `struct` or `union`)
    /// </summary>
    public CppClassKind classKind { get; set; }
    /// <summary>
    /// Gets or sets whether this record is ordinary, a primary template, a partial template, or a concrete specialization.
    /// </summary>
    public CppTemplateKind templateKind { get; set; }
    /// <inheritdoc/>
    public string name { get; set; }
    /// <summary>
    /// Gets or sets the target of the Objective-C category. Null if this class is not an <see cref = "CppClassKind.ObjCInterfaceCategory"/>.
    /// </summary>
    public CppClass? objCCategoryTargetClass { get; set; }
    /// <summary>
    /// Gets or sets the name of the Objective-C category. Empty if this class is not an <see cref = "CppClassKind.ObjCInterfaceCategory"/>
    /// </summary>
    public string objCCategoryName { get; set; }

    /// <summary>
    /// Gets the current qualified record name including template parameters or concrete specialization arguments.
    /// </summary>
    public override string fullName
    {
        get
        {
            StringBuilder sb = new StringBuilder();
            string fullparent = this.fullParentName;
            if (string.IsNullOrEmpty(fullparent))
            {
                sb.Append(this.name);
            }
            else
            {
                sb.Append($"{fullparent}::{this.name}");
            }

            if (this.templateKind == CppTemplateKind.TemplateClass || this.templateKind == CppTemplateKind.PartialTemplateClass)
            {
                sb.Append('<');
                for (int i = 0; i < this.templateParameters.Count; i++)
                {
                    var tp = this.templateParameters[i];
                    if (i != 0)
                    {
                        sb.Append(", ");
                    }

                    sb.Append(tp.ToString());
                }

                sb.Append('>');
            }
            else if (this.templateKind == CppTemplateKind.TemplateSpecializedClass)
            {
                sb.Append('<');
                for (int i = 0; i < this.templateSpecializedArguments.Count; i++)
                {
                    var ta = this.templateSpecializedArguments[i];
                    if (i != 0)
                    {
                        sb.Append(", ");
                    }

                    sb.Append(ta.argString);
                }

                sb.Append('>');
            }

            //else if(TemplateKind == CppTemplateKind.PartialTemplateClass)
            //{
            //    sb.Append('<');
            //    sb.Append('>');
            //}
            return sb.ToString();
        }
    }

    /// <inheritdoc/>
    public CppVisibility visibility { get; set; }
    /// <inheritdoc/>
    public List<CppAttribute> attributes { get; }
    /// <summary>
    /// Gets mutable source-token attributes recovered separately from native attribute cursors.
    /// </summary>
    public List<CppAttribute> tokenAttributes { get; } = [];
    /// <summary>
    /// Gets the mutable recognized annotation map owned by this record.
    /// </summary>
    public MetaAttributeMap metaAttributes { get; private set; } = new MetaAttributeMap();
    /// <summary>
    /// Gets or sets a boolean indicating if this type is a definition. If <c>false</c> the type was only declared but is not defined.
    /// </summary>
    public bool isDefinition { get; set; }
    /// <summary>
    /// Gets or sets the borrowed defining record node, or null when no definition has been resolved.
    /// </summary>
    public CppClass? definition { get; set; }
    /// <summary>
    /// Gets or sets a boolean indicating if this declaration is anonymous.
    /// </summary>
    public bool isAnonymous { get; set; }
    /// <summary>
    /// Get the base types of this type.
    /// </summary>
    public List<CppBaseType> baseTypes { get; }
    /// <summary>
    /// Get the Objective-C implemented protocols.
    /// </summary>
    public List<CppClass> objCImplementedProtocols { get; }
    /// <inheritdoc/>
    public CppContainerList<CppField> fields { get; }
    /// <inheritdoc/>
    public CppContainerList<CppProperty> properties { get; }
    /// <summary>
    /// Gets the constructors of this instance.
    /// </summary>
    public CppContainerList<CppFunction> constructors { get; set; }
    /// <summary>
    /// Gets the destructors of this instance.
    /// </summary>
    public CppContainerList<CppFunction> destructors { get; set; }
    /// <inheritdoc/>
    public CppContainerList<CppFunction> functions { get; }
    /// <inheritdoc/>
    public CppContainerList<CppEnum> enums { get; }
    /// <inheritdoc/>
    public CppContainerList<CppClass> classes { get; }
    /// <inheritdoc/>
    public CppContainerList<CppTypedef> typedefs { get; }
    /// <summary>
    /// Gets the Objective-C categories of this instance.
    /// </summary>
    public List<CppClass> objCCategories { get; }
    /// <inheritdoc/>
    public CppContainerList<CppType> templateParameters { get; }
    /// <summary>
    /// Gets mutable concrete specialization arguments in native template order.
    /// </summary>
    public List<CppTemplateArgument> templateSpecializedArguments { get; } = [];
    /// <summary>
    /// Gets the specialized class template of this instance.
    /// </summary>
    public CppClass? specializedTemplate { get; set; }
    /// <summary>
    /// Gets whether the current parent is another class/struct/union node.
    /// </summary>
    public bool isEmbeded => this.parent is CppClass;
    /// <summary>
    /// Gets or sets whether the native record has unresolved pure virtual methods.
    /// </summary>
    public bool isAbstract { get; set; }
    /// <summary>
    /// Gets or sets whether Clang reports a complete record definition.
    /// </summary>
    public bool isCompleteDefinition { get; set; }
    /// <summary>
    /// Gets whether the parser has visited and populated this record definition.
    /// </summary>
    public bool isDefined { get; internal set; }
    /// <summary>
    /// Gets or sets whether Clang classifies the record as plain-old-data for the selected language and target.
    /// </summary>
    public bool isPODType { get; set; }
    /// <inheritdoc/>
    public override int sizeOf { get; set; }
    /// <summary>
    /// Gets the alignment of this instance.
    /// </summary>
    public int alignOf { get; set; }

    /// <inheritdoc/>
    public override CppType GetCanonicalType()
    {
        return this;
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        var builder = new StringBuilder();
        switch (this.classKind)
        {
            case CppClassKind.Class:
                builder.Append("class ");
                break;
            case CppClassKind.Struct:
                builder.Append("struct ");
                break;
            case CppClassKind.Union:
                builder.Append("union ");
                break;
            case CppClassKind.ObjCInterface:
            case CppClassKind.ObjCInterfaceCategory:
                builder.Append("@interface ");
                break;
            case CppClassKind.ObjCProtocol:
                builder.Append("@protocol ");
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        if (!string.IsNullOrEmpty(this.name))
        {
            builder.Append(this.name);
        }

        //Add template arguments here
        if (this.templateKind != CppTemplateKind.NormalClass)
        {
            builder.Append('<');
            if (this.templateKind == CppTemplateKind.TemplateSpecializedClass)
            {
                for (var i = 0; i < this.templateSpecializedArguments.Count; i++)
                {
                    if (i > 0)
                        builder.Append(", ");
                    builder.Append(this.templateSpecializedArguments[i].ToString());
                }
            }
            else if (this.templateParameters.Count > 0)
            {
                for (var i = 0; i < this.templateParameters.Count; i++)
                {
                    if (i > 0)
                        builder.Append(", ");
                    builder.Append(this.templateParameters[i].ToString());
                }
            }

            builder.Append('>');
        }

        if (this.baseTypes.Count > 0)
        {
            builder.Append(" : ");
            for (var i = 0; i < this.baseTypes.Count; i++)
            {
                var baseType = this.baseTypes[i];
                if (i > 0)
                    builder.Append(", ");
                builder.Append(baseType);
            }
        }

        if (!string.IsNullOrEmpty(this.objCCategoryName))
        {
            builder.Append(" (").Append(this.objCCategoryName).Append(')');
        }

        if (this.objCImplementedProtocols.Count > 0)
        {
            builder.Append(" <");
            for (var i = 0; i < this.objCImplementedProtocols.Count; i++)
            {
                var protocol = this.objCImplementedProtocols[i];
                if (i > 0)
                    builder.Append(", ");
                builder.Append(protocol.name);
            }

            builder.Append('>');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Enumerates borrowed current declarations, then constructors and destructors, in their container order.
    /// </summary>
    public override IEnumerable<ICppDeclaration> children
    {
        get
        {
            foreach (var item in CppContainerHelper.Children(this))
            {
                yield return item;
            }

            foreach (var item in this.constructors)
            {
                yield return item;
            }

            foreach (var item in this.destructors)
            {
                yield return item;
            }
        }
    }
}
