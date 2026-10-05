using System.Diagnostics.CodeAnalysis;
using BGCS.CppAst.Model.Expressions;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using ClangSharp.Interop;

namespace BGCS.CppAst.Utilities;

/// <summary>
/// Internal class to iterate on tokens
/// </summary>
internal sealed class TokenIterator
{
    private readonly Tokenizer m_tokens;
    private int m_index;
    /// <summary>
    /// Initializes a new instance of <see cref = "TokenIterator"/>.
    /// </summary>
    public TokenIterator(Tokenizer tokens)
    {
        this.m_tokens = tokens;
    }

    /// <summary>
    /// Exposes public member <c>tokens.Cursor</c>.
    /// </summary>
    public CXCursor cursor => this.m_tokens.cursor;

    /// <summary>
    /// Executes public operation <c>Skip</c>.
    /// </summary>
    public bool Skip(string expectedText)
    {
        if (this.m_index < this.m_tokens.count)
        {
            if (this.m_tokens.GetString(this.m_index) == expectedText)
            {
                this.m_index++;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Executes public operation <c>PreviousToken</c>.
    /// </summary>
    public CppToken? PreviousToken()
    {
        if (this.m_index > 0)
        {
            return this.m_tokens[this.m_index - 1];
        }

        return null;
    }

    /// <summary>
    /// Executes public operation <c>Skip</c>.
    /// </summary>
    public bool Skip(params string[] expectedTokens)
    {
        var startIndex = this.m_index;
        foreach (var expectedToken in expectedTokens)
        {
            if (startIndex < this.m_tokens.count)
            {
                if (this.m_tokens.GetString(startIndex) == expectedToken)
                {
                    startIndex++;
                    continue;
                }
            }

            return false;
        }

        this.m_index = startIndex;
        return true;
    }

    /// <summary>
    /// Executes public operation <c>Find</c>.
    /// </summary>
    public bool Find(params string[] expectedTokens)
    {
        var startIndex = this.m_index;
    restart:
        while (startIndex < this.m_tokens.count)
        {
            var firstIndex = startIndex;
            foreach (var expectedToken in expectedTokens)
            {
                if (startIndex < this.m_tokens.count)
                {
                    if (this.m_tokens.GetString(startIndex) == expectedToken)
                    {
                        startIndex++;
                        continue;
                    }
                }

                startIndex = firstIndex + 1;
                goto restart;
            }

            this.m_index = firstIndex;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Executes public operation <c>Next</c>.
    /// </summary>
    public bool Next([NotNullWhen(true)] out CppToken? token)
    {
        token = null;
        if (this.m_index < this.m_tokens.count)
        {
            token = this.m_tokens[this.m_index];
            this.m_index++;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Exposes public member <c>tokens.Count</c>.
    /// </summary>
    public bool canPeek => this.m_index < this.m_tokens.count;

    /// <summary>
    /// Executes public operation <c>Next</c>.
    /// </summary>
    public bool Next()
    {
        if (this.m_index < this.m_tokens.count)
        {
            this.m_index++;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Executes public operation <c>Peek</c>.
    /// </summary>
    public CppToken? Peek()
    {
        if (this.m_index < this.m_tokens.count)
        {
            return this.m_tokens[this.m_index];
        }

        return null;
    }

    /// <summary>
    /// Executes public operation <c>PeekText</c>.
    /// </summary>
    public string? PeekText()
    {
        if (this.m_index < this.m_tokens.count)
        {
            return this.m_tokens.GetString(this.m_index);
        }

        return null;
    }
}
