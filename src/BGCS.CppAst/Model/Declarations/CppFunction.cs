// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.


using ClangSharp.Interop;
using BGCS.CppAst.AttributeUtils;
using BGCS.CppAst.Collections;
using BGCS.CppAst.Extensions;
using BGCS.CppAst.Model.Attributes;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Model.Types;
using System;
using System.Collections.Generic;
using System.Text;

namespace BGCS.CppAst.Model.Declarations;
/// <summary>
/// A C++ function/method declaration.
/// </summary>
public sealed class CppFunction : CppDeclaration, ICppMemberWithVisibility, ICppTemplateOwner, ICppContainer, ICppAttributeContainer
{
    /// <summary>
    /// Creates a new instance of a function/method with the specified name.
    /// </summary>
    /// <param name="cursor"></param>
    /// <param name="name">Name of this function/method.</param>
    public CppFunction(CXCursor cursor, string name) : base(cursor)
    {
        Name = name;
        Parameters = new CppContainerList<CppParameter>(this);
        TemplateParameters = new CppContainerList<CppType>(this);
        Attributes = [];
        TokenAttributes = [];
    }

    /// <inheritdoc />
    public CppVisibility Visibility { get; set; }

    /// <summary>
    /// Gets or sets the calling convention.
    /// </summary>
    public CppCallingConvention CallingConvention { get; set; }

    /// <summary>
    /// Gets the attached attributes.
    /// </summary>
    public List<CppAttribute> Attributes { get; }

    [Obsolete("TokenAttributes is deprecated. please use system attributes and annotate attributes")]
    /// <summary>
    /// Gets <c>TokenAttributes</c>.
    /// </summary>
    public List<CppAttribute> TokenAttributes { get; }

    /// <summary>
    /// Gets <c>MetaAttributes</c>.
    /// </summary>
    public MetaAttributeMap MetaAttributes { get; } = new MetaAttributeMap();

    /// <summary>
    /// Gets or sets the storage qualifier.
    /// </summary>
    public CppStorageQualifier StorageQualifier { get; set; }

    /// <summary>
    /// Gets or sets the linkage kind
    /// </summary>
    public CppLinkageKind LinkageKind { get; set; }

    /// <summary>
    /// Gets or sets whether this function is declared with <c>extern "C"</c> linkage specification.
    /// </summary>
    public bool IsExternC { get; set; }

    /// <summary>
    /// Gets or sets the return type.
    /// </summary>
    public CppType ReturnType { get; set; }

    /// <summary>
    /// Gets or sets a boolean indicating whether this method is a constructor method.
    /// </summary>
    public bool IsConstructor { get; set; }

    /// <summary>
    /// Gets or sets a boolean indicating whether this method is a destructor method.
    /// </summary>
    public bool IsDestructor { get; set; }

    /// <inheritdoc />
    public string Name { get; set; }

    /// <summary>
    /// Gets a list of the parameters.
    /// </summary>
    public CppContainerList<CppParameter> Parameters { get; }

    /// <summary>
    /// Exposes public member <c>DefaultParamCount</c>.
    /// </summary>
    public int DefaultParamCount
    {
        get
        {
            int default_count = 0;
            foreach (var param in Parameters)
            {
                if (param.InitExpression != null)
                {
                    default_count++;
                }
            }
            return default_count;
        }
    }

    /// <summary>
    /// Gets or sets the flags of this function.
    /// </summary>
    public CppFunctionFlags Flags { get; set; }

    /// <summary>
    /// Executes public operation <c>Member</c>.
    /// </summary>
    public bool IsCxxClassMethod => ((int)Flags & (int)CppFunctionFlags.Method) != 0;

    /// <summary>
    /// Executes public operation <c>Member</c>.
    /// </summary>
    public bool IsPureVirtual => ((int)Flags & (int)CppFunctionFlags.Pure) != 0;

    /// <summary>
    /// Executes public operation <c>Member</c>.
    /// </summary>
    public bool IsVirtual => ((int)Flags & (int)CppFunctionFlags.Virtual) != 0;

    /// <summary>
    /// Exposes public member <c>CppStorageQualifier.Static</c>.
    /// </summary>
    public bool IsStatic => StorageQualifier == CppStorageQualifier.Static;

    /// <summary>
    /// Executes public operation <c>Member</c>.
    /// </summary>
    public bool IsConst => ((int)Flags & (int)CppFunctionFlags.Const) != 0;

    /// <summary>
    /// Executes public operation <c>Member</c>.
    /// </summary>
    public bool IsFunctionTemplate => ((int)Flags & (int)CppFunctionFlags.FunctionTemplate) != 0;

    /// <inheritdoc />
    public CppContainerList<CppType> TemplateParameters { get; }

    /// <inheritdoc />
    public override string ToString()
    {
        StringBuilder builder = new();

        if (Visibility != CppVisibility.Default)
        {
            builder.Append(Visibility.ToString().ToLowerInvariant());
            builder.Append(' ');
        }

        if (StorageQualifier != CppStorageQualifier.None)
        {
            builder.Append(StorageQualifier.ToString().ToLowerInvariant());
            builder.Append(' ');
        }

        if ((Flags & CppFunctionFlags.Virtual) != 0)
        {
            builder.Append("virtual ");
        }

        if (!IsConstructor)
        {
            if (ReturnType != null)
            {
                builder.Append(ReturnType.GetDisplayName());
                builder.Append(' ');
            }
            else
            {
                builder.Append("void ");
            }
        }

        builder.Append(Name);
        builder.Append('(');
        for (var i = 0; i < Parameters.Count; i++)
        {
            var param = Parameters[i];
            if (i > 0) builder.Append(", ");
            builder.Append(param);
        }

        if ((Flags & CppFunctionFlags.Variadic) != 0)
        {
            builder.Append(", ...");
        }

        builder.Append(')');

        if ((Flags & CppFunctionFlags.Const) != 0)
        {
            builder.Append(" const");
        }

        if ((Flags & CppFunctionFlags.Pure) != 0)
        {
            builder.Append(" = 0");
        }
        return builder.ToString();
    }

    /// <inheritdoc />
    public IEnumerable<ICppDeclaration> Children => Parameters;
}
