using System.Diagnostics;
using System.Text;
using BGCS.CppAst.Model.Expressions;
using BGCS.CppAst.Parsing;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using ClangSharp.Interop;

namespace BGCS.CppAst.Utilities;

/// <summary>
/// Internal class to tokenize
/// </summary>
[DebuggerTypeProxy(typeof(TokenizerDebuggerType))]
internal class Tokenizer
{
    private readonly CXSourceRange m_range;
    private CppToken[]? m_cppTokens;
    /// <summary>
    /// Translation unit borrowed from the cursor; callers retain its lifetime while tokenization is in progress.
    /// </summary>
    protected readonly CXTranslationUnit tu;
    private readonly CXCursor m_cursor;
    /// <summary>
    /// Initializes a new instance of <see cref = "Tokenizer"/>.
    /// </summary>
    public Tokenizer(CXCursor cursor)
    {
        tu = cursor.TranslationUnit;
        this.m_range = GetRange(cursor);
        this.m_cursor = cursor;
    }

    /// <summary>
    /// Executes public operation <c>Tokenizer</c>.
    /// </summary>
    public Tokenizer(
        CXTranslationUnit tu,
        CXSourceRange range
    ) {
        this.tu = tu;
        this.m_range = range;
    }

    /// <summary>
    /// Exposes public member <c>cursor</c>.
    /// </summary>
    public CXCursor cursor => this.m_cursor;

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
    public int count
    {
        get
        {
            EnsureTokens();
            return this.m_cppTokens!.Length;
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
            return this.m_cppTokens![i];
        }
    }

    /// <summary>
    /// Returns computed data from <c>GetString</c>.
    /// </summary>
    public string GetString(int i)
    {
        EnsureTokens();
        return this.m_cppTokens![i].text;
    }

    /// <summary>
    /// Executes public operation <c>TokensToString</c>.
    /// </summary>
    public string TokensToString()
    {
        EnsureTokens();
        return this.m_cppTokens!.Length == 0 ? string.Empty : CppToken.TokensToString(this.m_cppTokens);
    }

    private void EnsureTokens()
    {
        if (this.m_cppTokens != null)
            return;
        var nativeTokens = tu.Tokenize(this.m_range);
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
                converted[i] = new CppToken(this.m_cursor, kind, CXUtil.GetTokenSpelling(token, tu))
                {
                    span = token.GetExtent(tu).ToSourceRange()
                };
            }

            this.m_cppTokens = converted;
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
        for (var cur = 0; cur < this.count; ++cur)
        {
            result.Append(GetString(cur));
            if (result.Length >= length)
                return result.ToString();
        }

        return result.ToString();
    }
}
