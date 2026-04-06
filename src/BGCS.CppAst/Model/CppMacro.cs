using System;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

using ClangSharp.Interop;
using BGCS.CppAst.Model.Expressions;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Parsing;
using System.Collections.Generic;
using System.Text;

namespace BGCS.CppAst.Model;
/// <summary>
/// A C++ Macro, only valid if the parser is initialized with <see cref="CppParserOptions.ParseMacros"/>
/// </summary>
public class CppMacro : CppElement, ICppMember
{
    /// <summary>
    /// Creates a new instance of a macro.
    /// </summary>
    /// <param name="cursor"></param>
    /// <param name="name"></param>
    public CppMacro(CXCursor cursor, string name) : base(cursor)
    {
        Name = name;
        Tokens = [];
    }

    /// <summary>
    /// Gets or sets the name of the macro.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the parameters of this macro (e.g `param1` and `param2` in `#define MY_MACRO(param1, param2)`)
    /// </summary>
    public List<string> Parameters { get; set; }

    /// <summary>
    /// Gets or sets the tokens of the value of the macro. The full string of the tokens is accessible via the <see cref="Value"/> property.
    /// </summary>
    /// <remarks>
    /// If tokens are updated, you need to call <see cref="UpdateValueFromTokens"/>
    /// </remarks>
    public List<CppToken> Tokens { get; }

    /// <summary>
    /// Gets a textual representation of the token values of this macro.
    /// </summary>
    public string Value { get; set; }

    /// <summary>
    /// Call this method to update the <see cref="Value"/> property from the list of <see cref="Tokens"/>
    /// </summary>
    public void UpdateValueFromTokens()
    {
        Value = CppToken.TokensToString(Tokens);
    }

    /// <summary>
    /// Executes public operation <c>ToString</c>.
    /// </summary>
    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.Append(Name);
        if (Parameters != null)
        {
            builder.Append('(');
            for (var i = 0; i < Parameters.Count; i++)
            {
                var parameter = Parameters[i];
                if (i > 0) builder.Append(", ");
                builder.Append(parameter);
            }

            builder.Append(')');
        }

        builder.Append(" = ").Append(Value);
        return builder.ToString();
    }
}
