using System;
using System.Collections.Generic;
using BGCS.CppAst.Model.Types;
using ClangSharp.Interop;

namespace BGCS.CppAst.Model.Templates;

/// <summary>Models a template parameter whose argument must itself be a template.</summary>
public sealed class CppTemplateParameterTemplate : CppType
{
    public CppTemplateParameterTemplate(CXCursor cursor, string name)
        : base(cursor, CppTypeKind.TemplateParameterTemplate)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public CppTemplateParameterTemplate(CX_TemplateArgument argument, string name,
        IEnumerable<CppType> parameters)
        : base(CXCursor.Null, CppTypeKind.TemplateParameterTemplate)
    {
        TemplateArgument = argument;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Parameters.AddRange(parameters ?? throw new ArgumentNullException(nameof(parameters)));
    }

    /// <summary>The declared template parameter name.</summary>
    public string Name { get; }

    /// <summary>The required template parameter signature.</summary>
    public List<CppType> Parameters { get; } = [];

    /// <summary>The selected argument when this parameter belongs to a specialization.</summary>
    public CX_TemplateArgument TemplateArgument { get; }

    public override int SizeOf
    {
        get => 0;
        set => throw new InvalidOperationException("A template template parameter has no object size.");
    }

    public override CppType GetCanonicalType() => this;

    public override string ToString() => Name;
}
