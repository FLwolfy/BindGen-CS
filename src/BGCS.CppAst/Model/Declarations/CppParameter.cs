// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using System;
using BGCS.CppAst.Extensions;
using BGCS.CppAst.Model.Expressions;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Model.Types;
using BGCS.CppAst.Utilities;
using ClangSharp.Interop;

namespace BGCS.CppAst.Model.Declarations;

/// <summary>
/// A C++ function parameter.
/// </summary>
public sealed class CppParameter : CppDeclaration, ICppMember
{
    /// <summary>
    /// Creates a mutable native parameter projection borrowing its compilation lifetime.
    /// </summary>
    /// <param name="cursor">
    /// The borrowed Clang cursor, valid only while its owning compilation remains alive; default creates a synthetic node.
    /// </param>
    /// <param name="type">
    /// The non-null borrowed native parameter type.
    /// </param>
    /// <param name="name">
    /// The non-null native identifier, empty for unnamed parameters.
    /// </param>
    public CppParameter(
        CXCursor cursor,
        CppType type,
        string name
    ) : base(cursor)
    {
        this.type = type ?? throw new ArgumentNullException(nameof(type));
        this.name = name ?? throw new ArgumentNullException(nameof(name));
        sourceTypeSpelling = cursor.kind == 0 ? string.Empty : CXUtil.GetTypeSpelling(cursor.Type);
    }

    /// <summary>
    /// Gets the type of this parameter.
    /// </summary>
    public CppType type { get; set; }

    /// <summary>
    /// Gets the original parameter type spelling captured by the parser, or an empty string for a constructed declaration.
    /// </summary>
    public string sourceTypeSpelling { get; }
    /// <summary>
    /// Gets the name of this parameter.
    /// </summary>
    public string name { get; set; }
    /// <summary>
    /// Gets or sets the default value.
    /// </summary>
    public CppValue? initValue { get; set; }
    /// <summary>
    /// Gets or sets the default value as an expression.
    /// </summary>
    public CppExpression? initExpression { get; set; }

    /// <summary>
    /// Formats the native parameter type, optional identifier, and current initializer expression.
    /// </summary>
    /// <returns>
    /// The current diagnostic declaration spelling, not a persisted binding identity.
    /// </returns>
    public override string ToString()
    {
        if (string.IsNullOrEmpty(this.name))
        {
            return this.initExpression != null ? $"{this.type.GetDisplayName()} = {this.initExpression}" : $"{this.type.GetDisplayName()}";
        }

        return this.initExpression != null ? $"{this.type.GetDisplayName()} {this.name} = {this.initExpression}" : $"{this.type.GetDisplayName()} {this.name}";
    }
}
