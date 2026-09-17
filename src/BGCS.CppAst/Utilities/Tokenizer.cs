using System;
using System.Collections.Generic;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

using ClangSharp.Interop;
using BGCS.CppAst.Model.Expressions;
using BGCS.CppAst.Parsing;
using System.Diagnostics;
using System.Text;

namespace BGCS.CppAst.Utilities;
/// <summary>
/// Internal class to tokenize
/// </summary>
[DebuggerTypeProxy(typeof(TokenizerDebuggerType))]
public class Tokenizer
{
    private readonly CXSourceRange range;
    private CppToken[]? cppTokens;
    protected readonly CXTranslationUnit tu;
    private readonly CXCursor cursor;

    /// <summary>
    /// Initializes a new instance of <see cref="Tokenizer"/>.
    /// </summary>
    public Tokenizer(CXCursor cursor)
    {
        tu = cursor.TranslationUnit;
        range = GetRange(cursor);
        this.cursor = cursor;
    }

    /// <summary>
    /// Executes public operation <c>Tokenizer</c>.
    /// </summary>
    public Tokenizer(CXTranslationUnit tu, CXSourceRange range)
    {
        this.tu = tu;
        this.range = range;
    }

    /// <summary>
    /// Exposes public member <c>cursor</c>.
    /// </summary>
    public CXCursor Cursor => cursor;

    /// <summary>
    /// Returns computed data from <c>GetRange</c>.
    /// </summary>
    public virtual CXSourceRange GetRange(CXCursor cursor)
    {
        return cursor.Extent;
    }

    /// <summary>
    /// Exposes public member <c>Count</c>.
    /// </summary>
    public int Count
    {
        get
        {
            EnsureTokens();
            return cppTokens!.Length;
        }
    }

    /// <summary>
    /// Exposes public member <c>i]</c>.
    /// </summary>
    public CppToken this[int i]
    {
        get
        {
            EnsureTokens();
            return cppTokens![i];
        }
    }

    /// <summary>
    /// Returns computed data from <c>GetString</c>.
    /// </summary>
    public string GetString(int i)
    {
        EnsureTokens();
        return cppTokens![i].Text;
    }

    /// <summary>
    /// Executes public operation <c>TokensToString</c>.
    /// </summary>
    public string TokensToString()
    {
        EnsureTokens();
        return cppTokens!.Length == 0 ? string.Empty : CppToken.TokensToString(cppTokens);
    }

    private void EnsureTokens()
    {
        if (cppTokens != null)
            return;
        var nativeTokens = tu.Tokenize(range);
        try
        {
            CppToken[] converted = new CppToken[nativeTokens.Length];
            for (int i = 0; i < nativeTokens.Length; i++)
            {
                var token = nativeTokens[i];
                CppTokenKind kind = token.Kind switch
                {
                    CXTokenKind.CXToken_Punctuation => CppTokenKind.Punctuation,
                    CXTokenKind.CXToken_Keyword => CppTokenKind.Keyword,
                    CXTokenKind.CXToken_Identifier => CppTokenKind.Identifier,
                    CXTokenKind.CXToken_Literal => CppTokenKind.Literal,
                    CXTokenKind.CXToken_Comment => CppTokenKind.Comment,
                    _ => 0
                };
                converted[i] = new CppToken(cursor, kind, CXUtil.GetTokenSpelling(token, tu))
                {
                    Span = token.GetExtent(tu).ToSourceRange()
                };
            }
            cppTokens = converted;
        }
        finally
        {
            tu.DisposeTokens(nativeTokens);
        }
    }

    /// <summary>
    /// Returns computed data from <c>GetStringForLength</c>.
    /// </summary>
    public string GetStringForLength(int length)
    {
        StringBuilder result = new(length);
        for (var cur = 0; cur < Count; ++cur)
        {
            result.Append(GetString(cur));
            if (result.Length >= length)
                return result.ToString();
        }
        return result.ToString();
    }
}

/// <summary>
/// Defines the public class <c>TokenizerDebuggerType</c>.
/// </summary>
public class TokenizerDebuggerType
{
    private readonly Tokenizer tokenizer;

    /// <summary>
    /// Initializes a new instance of <see cref="TokenizerDebuggerType"/>.
    /// </summary>
    public TokenizerDebuggerType(Tokenizer tokenizer)
    {
        this.tokenizer = tokenizer;
    }

    [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
    /// <summary>
    /// Exposes public member <c>Items</c>.
    /// </summary>
    public object[] Items
    {
        get
        {
            var array = new object[tokenizer.Count];
            for (int i = 0; i < tokenizer.Count; i++)
            {
                array[i] = tokenizer[i];
            }
            return array;
        }
    }
}
