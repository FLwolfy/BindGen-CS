using System;
using System.Collections.Generic;
using BGCS.CppAst.Model.Types;
using ClangSharp.Interop;

namespace BGCS.CppAst.Model.Templates;

/// <summary>Models a template parameter whose argument must itself be a template.</summary>
public sealed class CppTemplateParameterTemplate : CppType
{
    /// <summary>
    /// Models a declared template parameter while borrowing its native cursor.
    /// </summary>
    /// <param name="cursor">Native declaration cursor valid only during the owning compilation's lifetime.</param>
    /// <param name="name">Declared parameter name.</param>
    public CppTemplateParameterTemplate(
        CXCursor cursor,
        string name
    ) : base(cursor, CppTypeKind.TemplateParameterTemplate)
    {
        this.name = name ?? throw new ArgumentNullException(nameof(name));
    }

    /// <summary>
    /// Models a selected template argument and its required parameter signature.
    /// </summary>
    /// <param name="argument">Borrowed native template argument from the owning compilation.</param>
    /// <param name="name">Template parameter name.</param>
    /// <param name="parameters">Types composing the template parameter signature.</param>
    public CppTemplateParameterTemplate(
        CX_TemplateArgument argument,
        string name,
        IEnumerable<CppType> parameters
    ) : base(CXCursor.Null, CppTypeKind.TemplateParameterTemplate)
    {
        this.templateArgument = argument;
        this.name = name ?? throw new ArgumentNullException(nameof(name));
        this.parameters.AddRange(parameters ?? throw new ArgumentNullException(nameof(parameters)));
    }

    /// <summary>The declared template parameter name.</summary>
    public string name { get; }
    /// <summary>The required template parameter signature.</summary>
    public List<CppType> parameters { get; } = [];
    /// <summary>The selected argument when this parameter belongs to a specialization.</summary>
    public CX_TemplateArgument templateArgument { get; }
    /// <summary>
    /// Gets zero because a template parameter has no object storage; assigning a size is unsupported.
    /// </summary>
    /// <exception cref="InvalidOperationException">A caller attempts to assign an object size.</exception>
    public override int sizeOf { get => 0; set => throw new InvalidOperationException("A template template parameter has no object size."); }

    /// <inheritdoc/>
    public override CppType GetCanonicalType() => this;
    /// <inheritdoc/>
    public override string ToString() => this.name;
}
