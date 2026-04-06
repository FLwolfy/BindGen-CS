using System;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

using ClangSharp.Interop;
using BGCS.CppAst.Model.Expressions;

namespace BGCS.CppAst.Utilities;
/// <summary>
/// Internal class to iterate on tokens
/// </summary>
public class TokenIterator
{
    private readonly Tokenizer tokens;
    private int index;

    /// <summary>
    /// Initializes a new instance of <see cref="TokenIterator"/>.
    /// </summary>
    public TokenIterator(Tokenizer tokens)
    {
        this.tokens = tokens;
    }

    /// <summary>
    /// Exposes public member <c>tokens.Cursor</c>.
    /// </summary>
    public CXCursor Cursor => tokens.Cursor;

    /// <summary>
    /// Executes public operation <c>Skip</c>.
    /// </summary>
    public bool Skip(string expectedText)
    {
        if (index < tokens.Count)
        {
            if (tokens.GetString(index) == expectedText)
            {
                index++;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Executes public operation <c>PreviousToken</c>.
    /// </summary>
    public CppToken PreviousToken()
    {
        if (index > 0)
        {
            return tokens[index - 1];
        }

        return null;
    }

    /// <summary>
    /// Executes public operation <c>Skip</c>.
    /// </summary>
    public bool Skip(params string[] expectedTokens)
    {
        var startIndex = index;
        foreach (var expectedToken in expectedTokens)
        {
            if (startIndex < tokens.Count)
            {
                if (tokens.GetString(startIndex) == expectedToken)
                {
                    startIndex++;
                    continue;
                }
            }
            return false;
        }
        index = startIndex;
        return true;
    }

    /// <summary>
    /// Executes public operation <c>Find</c>.
    /// </summary>
    public bool Find(params string[] expectedTokens)
    {
        var startIndex = index;
    restart:
        while (startIndex < tokens.Count)
        {
            var firstIndex = startIndex;
            foreach (var expectedToken in expectedTokens)
            {
                if (startIndex < tokens.Count)
                {
                    if (tokens.GetString(startIndex) == expectedToken)
                    {
                        startIndex++;
                        continue;
                    }
                }
                startIndex = firstIndex + 1;
                goto restart;
            }
            index = firstIndex;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Executes public operation <c>Next</c>.
    /// </summary>
    public bool Next(out CppToken token)
    {
        token = null;
        if (index < tokens.Count)
        {
            token = tokens[index];
            index++;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Exposes public member <c>tokens.Count</c>.
    /// </summary>
    public bool CanPeek => index < tokens.Count;

    /// <summary>
    /// Executes public operation <c>Next</c>.
    /// </summary>
    public bool Next()
    {
        if (index < tokens.Count)
        {
            index++;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Executes public operation <c>Peek</c>.
    /// </summary>
    public CppToken Peek()
    {
        if (index < tokens.Count)
        {
            return tokens[index];
        }
        return null;
    }

    /// <summary>
    /// Executes public operation <c>PeekText</c>.
    /// </summary>
    public string PeekText()
    {
        if (index < tokens.Count)
        {
            return tokens.GetString(index);
        }
        return null;
    }
}
