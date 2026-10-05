// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using System;
using System.Text;
using BGCS.CppAst.Extensions;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Templates;
using ClangSharp.Interop;

namespace BGCS.CppAst.Model.Types;

/// <summary>
/// A C++ base type used by <see cref = "CppClass.baseTypes"/>
/// </summary>
public sealed class CppBaseType : CppElement
{
    /// <summary>
    /// Captures a borrowed native type relationship for attempt-local analysis.
    /// </summary>
    /// <param name="cursor">
    /// The borrowed Clang cursor, valid only while its compilation remains alive; default creates a synthetic node.
    /// </param>
    /// <param name="baseType">
    /// The non-null borrowed native base record type.
    /// </param>
    public CppBaseType(
        CXCursor cursor,
        CppType baseType
    ) : base(cursor)
    {
        this.type = baseType ?? throw new ArgumentNullException(nameof(baseType));
    }

    /// <summary>
    /// Gets or sets the visibility of this type.
    /// </summary>
    public CppVisibility visibility { get; set; }
    /// <summary>
    /// Gets or sets if this element is virtual.
    /// </summary>
    public bool isVirtual { get; set; }
    /// <summary>
    /// Gets the C++ type associated.
    /// </summary>
    public CppType type { get; }

    /// <inheritdoc/>
    public override string ToString()
    {
        var builder = new StringBuilder();
        if (this.visibility != CppVisibility.Default && this.visibility != CppVisibility.Public)
        {
            builder.Append(this.visibility.ToString().ToLowerInvariant()).Append(' ');
        }

        if (this.isVirtual)
        {
            builder.Append("virtual ");
        }

        builder.Append(this.type.GetDisplayName());
        var cls = this.type as CppClass;
        if (cls != null && cls.templateKind != CppTemplateKind.NormalClass)
        {
            builder.Append('<');
            if (cls.templateKind == CppTemplateKind.TemplateSpecializedClass)
            {
                for (var i = 0; i < cls.templateSpecializedArguments.Count; i++)
                {
                    if (i > 0)
                        builder.Append(", ");
                    builder.Append(cls.templateSpecializedArguments[i].ToString());
                }
            }
            else if (cls.templateKind == CppTemplateKind.TemplateClass)
            {
                for (var i = 0; i < cls.templateParameters.Count; i++)
                {
                    if (i > 0)
                        builder.Append(", ");
                    builder.Append(cls.templateParameters[i].ToString());
                }
            }

            builder.Append('>');
        }

        return builder.ToString();
    }
}
