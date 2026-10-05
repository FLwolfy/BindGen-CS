// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using System;
using BGCS.CppAst.Model.Types;
using ClangSharp.Interop;

namespace BGCS.CppAst.Model.Templates;

/// <summary>
/// A C++ template parameter type.
/// </summary>
public sealed class CppTemplateParameterNonType : CppType
{
    /// <summary>
    /// Captures a template parameter from a borrowed native declaration cursor.
    /// </summary>
    /// <param name="cursor">
    /// The borrowed Clang cursor, valid only while its owning compilation remains alive; default creates a synthetic node.
    /// </param>
    /// <param name="name">
    /// The non-null template parameter identifier.
    /// </param>
    /// <param name="templateNonType">
    /// The non-null borrowed native type accepted by this non-type template parameter.
    /// </param>
    public CppTemplateParameterNonType(
        CXCursor cursor,
        string name,
        CppType templateNonType
    ) : base(cursor, CppTypeKind.TemplateParameterNonType)
    {
        this.name = name ?? throw new ArgumentNullException(nameof(name));
        this.noneTemplateType = templateNonType ?? throw new ArgumentNullException(nameof(templateNonType));
    }

    /// <summary>
    /// Captures a template parameter projection from a borrowed native template argument.
    /// </summary>
    /// <param name="templateArgument">
    /// The borrowed native template argument, valid while its compilation remains alive.
    /// </param>
    /// <param name="name">
    /// The non-null template parameter identifier.
    /// </param>
    /// <param name="templateNonType">
    /// The non-null borrowed native type accepted by this non-type template parameter.
    /// </param>
    public CppTemplateParameterNonType(
        CX_TemplateArgument templateArgument,
        string name,
        CppType templateNonType
    ) : base(CXCursor.Null, CppTypeKind.TemplateParameterNonType)
    {
        this.templateArgument = templateArgument;
        this.name = name ?? throw new ArgumentNullException(nameof(name));
        this.noneTemplateType = templateNonType ?? throw new ArgumentNullException(nameof(templateNonType));
    }

    /// <summary>
    /// Gets or sets the borrowed native template argument; its owning compilation must remain alive.
    /// </summary>
    public CX_TemplateArgument templateArgument { get; set; }
    /// <summary>
    /// Name of the template parameter.
    /// </summary>
    public string name { get; }
    /// <summary>
    /// Gets the borrowed native value type accepted by this non-type template parameter.
    /// </summary>
    public CppType noneTemplateType { get; }

    private bool Equals(CppTemplateParameterNonType other)
    {
        return base.Equals(other) && this.name.Equals(other.name) && this.noneTemplateType.Equals(other.noneTemplateType);
    }

    /// <inheritdoc/>
    public override int sizeOf { get => 0; set => throw new InvalidOperationException("This type does not support SizeOf"); }

    /// <inheritdoc/>
    public override CppType GetCanonicalType() => this;
    /// <inheritdoc/>
    public override string ToString() => $"{this.noneTemplateType.ToString()} {this.name}";
}
