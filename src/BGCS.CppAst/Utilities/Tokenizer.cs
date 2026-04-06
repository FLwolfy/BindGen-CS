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
    private CppToken[] cppTokens;
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
            var tokens = tu.Tokenize(range);
            int length = tokens.Length;
            tu.DisposeTokens(tokens);
            return length;
        }
    }

    /// <summary>
    /// Exposes public member <c>i]</c>.
    /// </summary>
    public CppToken this[int i]
    {
        get
        {
            // Only create a tokenizer if necessary
            cppTokens ??= new CppToken[Count];

            ref var cppToken = ref cppTokens[i];
            if (cppToken != null)
            {
                return cppToken;
            }
            var tokens = tu.Tokenize(range);
            var token = tokens[i];

            CppTokenKind cppTokenKind = 0;
            switch (token.Kind)
            {
                case CXTokenKind.CXToken_Punctuation:
                    cppTokenKind = CppTokenKind.Punctuation;
                    break;

                case CXTokenKind.CXToken_Keyword:
                    cppTokenKind = CppTokenKind.Keyword;
                    break;

                case CXTokenKind.CXToken_Identifier:
                    cppTokenKind = CppTokenKind.Identifier;
                    break;

                case CXTokenKind.CXToken_Literal:
                    cppTokenKind = CppTokenKind.Literal;
                    break;

                case CXTokenKind.CXToken_Comment:
                    cppTokenKind = CppTokenKind.Comment;
                    break;

                default:
                    break;
            }

            var tokenStr = CXUtil.GetTokenSpelling(token, tu);
            var tokenLocation = token.GetLocation(tu);

            var tokenRange = token.GetExtent(tu);
            cppToken = new CppToken(cursor, cppTokenKind, tokenStr)
            {
                Span = tokenRange.ToSourceRange()
            };
            tu.DisposeTokens(tokens);
            return cppToken;
        }
    }

    /// <summary>
    /// Returns computed data from <c>GetString</c>.
    /// </summary>
    public string GetString(int i)
    {
        var tokens = tu.Tokenize(range);
        var TokenSpelling = CXUtil.GetTokenSpelling(tokens[i], tu);
        tu.DisposeTokens(tokens);
        return TokenSpelling;
    }

    /// <summary>
    /// Executes public operation <c>TokensToString</c>.
    /// </summary>
    public string TokensToString()
    {
        int length = Count;
        if (length <= 0)
        {
            return null;
        }

        List<CppToken> tokens = new(length);

        for (int i = 0; i < length; i++)
        {
            tokens.Add(this[i]);
        }

        return CppToken.TokensToString(tokens);
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
