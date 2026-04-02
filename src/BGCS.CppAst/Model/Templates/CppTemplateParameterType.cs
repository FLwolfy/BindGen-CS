// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.


using ClangSharp.Interop;
using BGCS.CppAst.Model.Types;
using System;

namespace BGCS.CppAst.Model.Templates;
/// <summary>
/// A C++ template parameter type.
/// </summary>
public sealed class CppTemplateParameterType : CppType
{
    /// <summary>
    /// Constructor of this template parameter type.
    /// </summary>
    /// <param name="cursor"></param>
    /// <param name="name"></param>
    public CppTemplateParameterType(CXCursor cursor, string name) : base(cursor, CppTypeKind.TemplateParameterType)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public CppTemplateParameterType(CX_TemplateArgument templateArgument, string name) : base(CXCursor.Null, CppTypeKind.TemplateParameterType)
    {
        TemplateArgument = templateArgument;
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public CX_TemplateArgument TemplateArgument { get; set; }

    /// <summary>
    /// Name of the template parameter.
    /// </summary>
    public string Name { get; }

    private bool Equals(CppTemplateParameterType other)
    {
        return base.Equals(other) && Name.Equals(other.Name);
    }

    /// <inheritdoc />
    public override int SizeOf
    {
        get => 0;
        set => throw new InvalidOperationException("This type does not support SizeOf");
    }

    /// <inheritdoc />
    public override CppType GetCanonicalType() => this;

    /// <inheritdoc />
    public override string ToString() => Name;
}
