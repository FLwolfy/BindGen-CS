// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using System.Collections.Generic;
using System.Text;
using BGCS.CppAst.AttributeParsing;
using BGCS.CppAst.Collections;
using BGCS.CppAst.Extensions;
using BGCS.CppAst.Model.Attributes;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Model.Types;
using BGCS.CppAst.Utilities;
using ClangSharp.Interop;

namespace BGCS.CppAst.Model.Declarations;

/// <summary>
/// A C++ function/method declaration.
/// </summary>
public sealed class CppFunction : CppDeclaration, ICppMemberWithVisibility, ICppTemplateOwner, ICppContainer, ICppAttributeContainer
{
    /// <summary>
    /// Creates a mutable native callable projection with empty owned child collections.
    /// </summary>
    /// <param name="cursor">
    /// The borrowed Clang cursor, valid only while its owning compilation remains alive; default creates a synthetic node.
    /// </param>
    /// <param name="name">
    /// The native declaration identifier, empty for an unnamed declaration.
    /// </param>
    public CppFunction(
        CXCursor cursor,
        string name
    ) : base(cursor)
    {
        this.name = name;
        this.parameters = new CppContainerList<CppParameter>(this);
        this.templateParameters = new CppContainerList<CppType>(this);
        this.attributes = [];
        this.returnType = CppPrimitiveType.@void;
        returnTypeSpelling = cursor.kind == 0 ? string.Empty : CXUtil.GetTypeSpelling(cursor.ResultType);
    }

    /// <inheritdoc/>
    public CppVisibility visibility { get; set; }
    /// <summary>
    /// Gets or sets the calling convention.
    /// </summary>
    public CppCallingConvention callingConvention { get; set; }
    /// <summary>
    /// Gets the attached attributes.
    /// </summary>
    public List<CppAttribute> attributes { get; }
    /// <summary>
    /// Gets mutable source-token attributes recovered separately from native attribute cursors.
    /// </summary>
    public List<CppAttribute> tokenAttributes { get; } = [];
    /// <summary>
    /// Gets the mutable recognized annotation map owned by this callable.
    /// </summary>
    public MetaAttributeMap metaAttributes { get; } = new MetaAttributeMap();
    /// <summary>
    /// Gets or sets the storage qualifier.
    /// </summary>
    public CppStorageQualifier storageQualifier { get; set; }
    /// <summary>
    /// Gets or sets the linkage kind
    /// </summary>
    public CppLinkageKind linkageKind { get; set; }
    /// <summary>
    /// Gets or sets whether this function is declared with <c>extern "C"</c> linkage specification.
    /// </summary>
    public bool isExternC { get; set; }
    /// <summary>
    /// Gets or sets the return type.
    /// </summary>
    public CppType returnType { get; set; }

    /// <summary>
    /// Gets the original return type spelling captured by the parser, or an empty string for a constructed declaration.
    /// </summary>
    public string returnTypeSpelling { get; }
    /// <summary>
    /// Gets or sets whether this declaration constructs an instance of its owning native class.
    /// </summary>
    public bool isConstructor { get; set; }
    /// <summary>
    /// Gets or sets a boolean indicating whether this method is a destructor method.
    /// </summary>
    public bool isDestructor { get; set; }
    /// <inheritdoc/>
    public string name { get; set; }
    /// <summary>
    /// Gets a list of the parameters.
    /// </summary>
    public CppContainerList<CppParameter> parameters { get; }

    /// <summary>
    /// Gets the number of current parameters with initializer expressions; evaluated from the mutable parameter list.
    /// </summary>
    public int defaultParamCount
    {
        get
        {
            int default_count = 0;
            foreach (var param in this.parameters)
            {
                if (param.initExpression != null)
                {
                    default_count++;
                }
            }

            return default_count;
        }
    }

    /// <summary>
    /// Gets or sets the native callable classification flags populated by parsing.
    /// </summary>
    public CppFunctionFlags flags { get; set; }
    /// <summary>
    /// Gets whether the callable flags classify this declaration as a C++ class method.
    /// </summary>
    public bool isCxxClassMethod => ((int)this.flags & (int)CppFunctionFlags.Method) != 0;
    /// <summary>
    /// Gets whether the callable flags mark this method as pure virtual.
    /// </summary>
    public bool isPureVirtual => ((int)this.flags & (int)CppFunctionFlags.Pure) != 0;
    /// <summary>
    /// Gets whether the callable flags mark this method as virtual.
    /// </summary>
    public bool isVirtual => ((int)this.flags & (int)CppFunctionFlags.Virtual) != 0;
    /// <summary>
    /// Gets whether the storage qualifier exactly selects a static callable.
    /// </summary>
    public bool isStatic => this.storageQualifier == CppStorageQualifier.Static;
    /// <summary>
    /// Gets whether the callable flags mark this method as const-qualified.
    /// </summary>
    public bool isConst => ((int)this.flags & (int)CppFunctionFlags.Const) != 0;
    /// <summary>
    /// Gets whether the callable flags mark this declaration as a function template.
    /// </summary>
    public bool isFunctionTemplate => ((int)this.flags & (int)CppFunctionFlags.FunctionTemplate) != 0;
    /// <inheritdoc/>
    public CppContainerList<CppType> templateParameters { get; }

    /// <inheritdoc/>
    public override string ToString()
    {
        StringBuilder builder = new();
        if (this.visibility != CppVisibility.Default)
        {
            builder.Append(this.visibility.ToString().ToLowerInvariant());
            builder.Append(' ');
        }

        if (this.storageQualifier != CppStorageQualifier.None)
        {
            builder.Append(this.storageQualifier.ToString().ToLowerInvariant());
            builder.Append(' ');
        }

        if ((this.flags & CppFunctionFlags.Virtual) != 0)
        {
            builder.Append("virtual ");
        }

        if (!this.isConstructor)
        {
            if (this.returnType != null)
            {
                builder.Append(this.returnType.GetDisplayName());
                builder.Append(' ');
            }
            else
            {
                builder.Append("void ");
            }
        }

        builder.Append(this.name);
        builder.Append('(');
        for (var i = 0; i < this.parameters.Count; i++)
        {
            var param = this.parameters[i];
            if (i > 0)
                builder.Append(", ");
            builder.Append(param);
        }

        if ((this.flags & CppFunctionFlags.Variadic) != 0)
        {
            builder.Append(", ...");
        }

        builder.Append(')');
        if ((this.flags & CppFunctionFlags.Const) != 0)
        {
            builder.Append(" const");
        }

        if ((this.flags & CppFunctionFlags.Pure) != 0)
        {
            builder.Append(" = 0");
        }

        return builder.ToString();
    }

    /// <inheritdoc/>
    public IEnumerable<ICppDeclaration> children => this.parameters;
}
