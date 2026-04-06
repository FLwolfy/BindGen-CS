// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

using ClangSharp.Interop;
using BGCS.CppAst.Extensions;
using BGCS.CppAst.Model.Expressions;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Model.Types;
using System;

namespace BGCS.CppAst.Model.Declarations;
/// <summary>
/// A C++ function parameter.
/// </summary>
public sealed class CppParameter : CppDeclaration, ICppMember
{
    /// <summary>
    /// Creates a new instance of a C++ function parameter.
    /// </summary>
    /// <param name="type">Type of the parameter.</param>
    /// <param name="name">Name of the parameter</param>
    public CppParameter(CXCursor cursor, CppType type, string name) : base(cursor)
    {
        Type = type ?? throw new ArgumentNullException(nameof(type));
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    /// <summary>
    /// Gets the type of this parameter.
    /// </summary>
    public CppType Type { get; set; }

    /// <summary>
    /// Gets the name of this parameter.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the default value.
    /// </summary>
    public CppValue? InitValue { get; set; }

    /// <summary>
    /// Gets or sets the default value as an expression.
    /// </summary>
    public CppExpression? InitExpression { get; set; }

    /// <summary>
    /// Executes public operation <c>ToString</c>.
    /// </summary>
    public override string ToString()
    {
        if (string.IsNullOrEmpty(Name))
        {
            return InitExpression != null ? $"{Type.GetDisplayName()} = {InitExpression}" : $"{Type.GetDisplayName()}";
        }

        return InitExpression != null ? $"{Type.GetDisplayName()} {Name} = {InitExpression}" : $"{Type.GetDisplayName()} {Name}";
    }
}
