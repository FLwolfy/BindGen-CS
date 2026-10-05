using System.Collections.Generic;
using System.Text;
using BGCS.CppAst.Model.Expressions;
using BGCS.CppAst.Model.Interfaces;
using BGCS.CppAst.Parsing;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using ClangSharp.Interop;

namespace BGCS.CppAst.Model;

/// <summary>
/// A C++ Macro, only valid if the parser is initialized with <see cref = "CppParserOptions.parseMacros"/>
/// </summary>
public class CppMacro : CppElement, ICppMember
{
    /// <summary>
    /// Creates a mutable native preprocessor macro projection borrowing its compilation lifetime.
    /// </summary>
    /// <param name="cursor">
    /// The borrowed Clang cursor, valid only while its owning compilation remains alive; default creates a synthetic node.
    /// </param>
    /// <param name="name">
    /// The macro identifier; tokens, parameters, and replacement text are populated by parsing.
    /// </param>
    public CppMacro(
        CXCursor cursor,
        string name
    ) : base(cursor)
    {
        this.name = name;
        this.tokens = [];
        this.parameters = [];
        this.value = string.Empty;
    }

    /// <summary>
    /// Gets or sets the name of the macro.
    /// </summary>
    public string name { get; set; }
    /// <summary>
    /// Gets or sets the parameters of this macro (e.g `param1` and `param2` in `#define MY_MACRO(param1, param2)`)
    /// </summary>
    public List<string>? parameters { get; set; }
    /// <summary>
    /// Gets or sets the tokens of the value of the macro. The full string of the tokens is accessible via the <see cref = "value"/> property.
    /// </summary>
    /// <remarks>
    /// If tokens are updated, you need to call <see cref = "UpdateValueFromTokens"/>
    /// </remarks>
    public List<CppToken> tokens { get; }
    /// <summary>
    /// Gets a textual representation of the token values of this macro.
    /// </summary>
    public string value { get; set; }

    /// <summary>
    /// Recomputes replacement text from current macro tokens, omitting comments and separating adjacent identifier or keyword tokens.
    /// </summary>
    public void UpdateValueFromTokens()
    {
        this.value = CppToken.TokensToString(this.tokens);
    }

    /// <summary>
    /// Formats the current native macro name, optional parameters, and replacement text.
    /// </summary>
    /// <returns>
    /// The current diagnostic declaration spelling, not a persisted binding identity.
    /// </returns>
    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.Append(this.name);
        if (this.parameters != null)
        {
            builder.Append('(');
            for (var i = 0; i < this.parameters.Count; i++)
            {
                var parameter = this.parameters[i];
                if (i > 0)
                    builder.Append(", ");
                builder.Append(parameter);
            }

            builder.Append(')');
        }

        builder.Append(" = ").Append(this.value);
        return builder.ToString();
    }
}
