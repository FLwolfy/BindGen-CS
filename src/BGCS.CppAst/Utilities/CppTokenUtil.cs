using System;
using System.Collections.Generic;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

using ClangSharp.Interop;
using BGCS.CppAst.Collections;
using BGCS.CppAst.Extensions;
using BGCS.CppAst.Model.Attributes;
using BGCS.CppAst.Model.Expressions;
using BGCS.CppAst.Model.Metadata;
using System.Diagnostics;
using System.Text;

namespace BGCS.CppAst.Utilities;
internal static unsafe class CppTokenUtil
{
    public static void ParseCursorAttributes(CppGlobalDeclarationContainer globalContainer, CXCursor cursor, ref List<CppAttribute> attributes)
    {
        var tokenizer = new AttributeTokenizer(cursor);
        var tokenIt = new TokenIterator(tokenizer);

        // if this is a template then we need to skip that ?
        if (tokenIt.CanPeek && tokenIt.PeekText() == "template")
            SkipTemplates(tokenIt);

        while (tokenIt.CanPeek)
        {
            if (ParseAttributes(globalContainer, tokenIt, ref attributes))
            {
                continue;
            }

            // If we have a keyword, try to skip it and process following elements
            // for example attribute put right after a struct __declspec(uuid("...")) Test {...}
            if (tokenIt.Peek().Kind == CppTokenKind.Keyword)
            {
                tokenIt.Next();
                continue;
            }
            break;
        }
    }

    public static void ParseFunctionAttributes(CppGlobalDeclarationContainer globalContainer, CXCursor cursor, string functionName, ref List<CppAttribute> attributes)
    {
        // TODO: This function is not 100% correct when parsing tokens up to the function name
        // we assume to find the function name immediately followed by a `(`
        // but some return type parameter could actually interfere with that
        // Ideally we would need to parse more properly return type and skip parenthesis for example
        AttributeTokenizer tokenizer = new(cursor);
        TokenIterator tokenIt = new(tokenizer);

        // if this is a template then we need to skip that ?
        if (tokenIt.CanPeek && tokenIt.PeekText() == "template")
            SkipTemplates(tokenIt);

        // Parse leading attributes
        while (tokenIt.CanPeek)
        {
            if (ParseAttributes(globalContainer, tokenIt, ref attributes))
            {
                continue;
            }
            break;
        }

        if (!tokenIt.CanPeek)
        {
            return;
        }

        // Find function name (We only support simple function name declaration)
        if (!tokenIt.Find(functionName, "("))
        {
            return;
        }

        Debug.Assert(tokenIt.PeekText() == functionName);
        tokenIt.Next();
        Debug.Assert(tokenIt.PeekText() == "(");
        tokenIt.Next();

        int parentCount = 1;
        while (parentCount > 0 && tokenIt.CanPeek)
        {
            var text = tokenIt.PeekText();
            if (text == "(")
            {
                parentCount++;
            }
            else if (text == ")")
            {
                parentCount--;
            }
            tokenIt.Next();
        }

        if (parentCount != 0)
        {
            return;
        }

        while (tokenIt.CanPeek)
        {
            if (ParseAttributes(globalContainer, tokenIt, ref attributes))
            {
                continue;
            }
            // Skip the token if we can parse it.
            tokenIt.Next();
        }

        return;
    }

    public static void ParseAttributesInRange(CppGlobalDeclarationContainer globalContainer, CXTranslationUnit tu, CXSourceRange range, ref List<CppAttribute> collectAttributes)
    {
        AttributeTokenizer tokenizer = new(tu, range);
        TokenIterator tokenIt = new(tokenizer);
        StringBuilder sb = new();
        while (tokenIt.CanPeek)
        {
            sb.Append(tokenIt.PeekText());
            tokenIt.Next();
        }

        // if this is a template then we need to skip that ?
        if (tokenIt.CanPeek && tokenIt.PeekText() == "template")
            SkipTemplates(tokenIt);

        while (tokenIt.CanPeek)
        {
            if (ParseAttributes(globalContainer, tokenIt, ref collectAttributes))
            {
                continue;
            }

            // If we have a keyword, try to skip it and process following elements
            // for example attribute put right after a struct __declspec(uuid("...")) Test {...}
            if (tokenIt.Peek().Kind == CppTokenKind.Keyword)
            {
                tokenIt.Next();
                continue;
            }
            break;
        }
    }

    private static int SkipWhiteSpace(ReadOnlySpan<byte> cnt, int cntOffset)
    {
        while (cntOffset > 0)
        {
            char ch = (char)cnt[cntOffset];
            if (ch == ' ' || ch == '\r' || ch == '\n' || ch == '\t')
            {
                cntOffset--;
            }
            else
            {
                break;
            }
        }

        return cntOffset;
    }

    private static int ToLineStart(ReadOnlySpan<byte> cnt, int cntOffset)
    {
        for (int i = cntOffset; i >= 0; i--)
        {
            char ch = (char)cnt[i];
            if (ch == '\n')
            {
                return i + 1;
            }
        }
        return 0;
    }

    private static bool IsAttributeEnd(ReadOnlySpan<byte> cnt, int cntOffset)
    {
        if (cntOffset < 1) return false;

        char ch0 = (char)cnt[cntOffset];
        char ch1 = (char)cnt[cntOffset - 1];

        return ch0 == ch1 && ch0 == ']';
    }

    private static bool IsAttributeStart(ReadOnlySpan<byte> cnt, int cntOffset)
    {
        if (cntOffset < 1) return false;

        char ch0 = (char)cnt[cntOffset];
        char ch1 = (char)cnt[cntOffset - 1];

        return ch0 == ch1 && ch0 == '[';
    }

    private static bool SeekAttributeStartSingleChar(ReadOnlySpan<byte> cnt, int cntOffset, out int outSeekOffset)
    {
        outSeekOffset = cntOffset;
        while (cntOffset > 0)
        {
            char ch = (char)cnt[cntOffset];
            if (ch == '[')
            {
                outSeekOffset = cntOffset;
                return true;
            }
            cntOffset--;
        }
        return false;
    }

    private static int SkipAttributeStartOrEnd(ReadOnlySpan<byte> cnt, int cntOffset)
    {
        cntOffset -= 2;
        return cntOffset;
    }

    private static string QueryLineContent(ReadOnlySpan<byte> cnt, int startOffset, int endOffset)
    {
        StringBuilder sb = new();
        for (int i = startOffset; i <= endOffset; i++)
        {
            sb.Append((char)cnt[i]);
        }
        return sb.ToString();
    }

    public static bool TryToSeekOnlineAttributes(CXCursor cursor, out CXSourceRange range)
    {
        CXSourceLocation location = cursor.Extent.Start;
        location.GetFileLocation(out var file, out var line, out var column, out var offset);
        var contents = cursor.TranslationUnit.GetFileContents(file, out var fileSize);

        AttributeLexerParseStatus status = AttributeLexerParseStatus.SeekAttributeEnd;
        int offsetStart = (int)offset - 1;   //Try to ignore start char here
        int lastSeekOffset = offsetStart;
        int curOffset = offsetStart;
        while (curOffset > 0)
        {
            curOffset = SkipWhiteSpace(contents, curOffset);

            switch (status)
            {
                case AttributeLexerParseStatus.SeekAttributeEnd:
                    {
                        if (!IsAttributeEnd(contents, curOffset))
                        {
                            status = AttributeLexerParseStatus.Error;
                        }
                        else
                        {
                            curOffset = SkipAttributeStartOrEnd(contents, curOffset);
                            status = AttributeLexerParseStatus.SeekAttributeStart;
                        }
                    }
                    break;

                case AttributeLexerParseStatus.SeekAttributeStart:
                    {
                        if (!SeekAttributeStartSingleChar(contents, curOffset, out var queryOffset))
                        {
                            status = AttributeLexerParseStatus.Error;
                        }
                        else
                        {
                            if (IsAttributeStart(contents, queryOffset))
                            {
                                curOffset = SkipAttributeStartOrEnd(contents, queryOffset);
                                lastSeekOffset = curOffset + 1;
                                status = AttributeLexerParseStatus.SeekAttributeEnd;
                            }
                            else
                            {
                                status = AttributeLexerParseStatus.Error;
                            }
                        }
                    }
                    break;
            }

            if (status == AttributeLexerParseStatus.Error)
            {
                break;
            }
        }
        if (lastSeekOffset == offsetStart)
        {
            range = new CXSourceRange();
            return false;
        }
        else
        {
            var startLoc = cursor.TranslationUnit.GetLocationForOffset(file, (uint)lastSeekOffset);
            var endLoc = cursor.TranslationUnit.GetLocationForOffset(file, (uint)offsetStart);
            range = clang.getRange(startLoc, endLoc);
            return true;
        }
    }

    #region "Private Functions"

    private static void SkipTemplates(TokenIterator iter)
    {
        if (iter.CanPeek)
        {
            if (iter.Skip("template"))
            {
                iter.Next(); // skip the first >
                int parentCount = 1;
                while (parentCount > 0 && iter.CanPeek)
                {
                    var text = iter.PeekText();
                    if (text == ">")
                    {
                        parentCount--;
                    }
                    iter.Next();
                }
            }
        }
    }

    private enum AttributeLexerParseStatus
    {
        SeekAttributeEnd,
        SeekAttributeStart,
        Error,
    }

    private static (string, string) GetNameSpaceAndAttribute(string fullAttribute)
    {
        string[] colons = { "::" };
        string[] tokens = fullAttribute.Split(colons, StringSplitOptions.None);
        if (tokens.Length == 2)
        {
            return (tokens[0], tokens[1]);
        }
        else
        {
            return (null, tokens[0]);
        }
    }

    private static (string, string) GetNameAndArguments(string name)
    {
        if (name.Contains("("))
        {
            char[] seperator = { '(' };
            var argumentTokens = name.Split(seperator, 2);
            var length = argumentTokens[1].LastIndexOf(')');
            string argument = null;
            if (length > 0)
            {
                argument = argumentTokens[1].Substring(0, length);
            }
            return (argumentTokens[0], argument);
        }
        else
        {
            return (name, null);
        }
    }

    private static bool ParseAttributes(CppGlobalDeclarationContainer globalContainer, TokenIterator tokenIt, ref List<CppAttribute> attributes)
    {
        // Parse C++ attributes
        // [[<attribute>]]
        if (tokenIt.Skip("[", "["))
        {
            while (ParseAttribute(tokenIt, out var attribute))
            {
                if (attributes == null)
                {
                    attributes = [];
                }
                attributes.Add(attribute);

                tokenIt.Skip(",");
            }

            return tokenIt.Skip("]", "]");
        }

        // Parse GCC or clang attributes
        // __attribute__((<attribute>))
        if (tokenIt.Skip("__attribute__", "(", "("))
        {
            while (ParseAttribute(tokenIt, out var attribute))
            {
                if (attributes == null)
                {
                    attributes = [];
                }
                attributes.Add(attribute);

                tokenIt.Skip(",");
            }

            return tokenIt.Skip(")", ")");
        }

        // Parse MSVC attributes
        // __declspec(<attribute>)
        if (tokenIt.Skip("__declspec", "("))
        {
            while (ParseAttribute(tokenIt, out var attribute))
            {
                if (attributes == null)
                {
                    attributes = [];
                }
                attributes.Add(attribute);

                tokenIt.Skip(",");
            }
            return tokenIt.Skip(")");
        }

        // Parse C++11 alignas attribute
        // alignas(expression)
        if (tokenIt.PeekText() == "alignas")
        {
            while (ParseAttribute(tokenIt, out var attribute))
            {
                if (attributes == null)
                {
                    attributes = [];
                }
                attributes.Add(attribute);

                break;
            }

            return tokenIt.Skip(")"); ;
        }

        // See if we have a macro
        var value = tokenIt.PeekText();
        var macro = globalContainer.Macros.Find(v => v.Name == value);
        if (macro != null)
        {
            if (macro.Value.StartsWith("[[") && macro.Value.EndsWith("]]"))
            {
                CppAttribute attribute = null;
                var fullAttribute = macro.Value.Substring(2, macro.Value.Length - 4);
                var (scope, name) = GetNameSpaceAndAttribute(fullAttribute);
                var (attributeName, arguments) = GetNameAndArguments(name);

                attribute = new CppAttribute(tokenIt.Cursor, attributeName, AttributeKind.TokenAttribute);
                attribute.Scope = scope;
                attribute.Arguments = arguments;

                if (attributes == null)
                {
                    attributes = [];
                }
                attributes.Add(attribute);
                tokenIt.Next();
                return true;
            }
        }

        return false;
    }

    private static bool ParseAttribute(TokenIterator tokenIt, out CppAttribute attribute)
    {
        // (identifier ::)? identifier ('(' tokens ')' )? (...)?
        attribute = null;
        var token = tokenIt.Peek();
        if (token == null || !token.Kind.IsIdentifierOrKeyword())
        {
            return false;
        }
        tokenIt.Next(out token);

        var firstToken = token;

        // try (identifier ::)?
        string scope = null;
        if (tokenIt.Skip("::"))
        {
            scope = token.Text;

            token = tokenIt.Peek();
            if (token == null || !token.Kind.IsIdentifierOrKeyword())
            {
                return false;
            }
            tokenIt.Next(out token);
        }

        // identifier
        string tokenIdentifier = token.Text;

        string arguments = null;

        // ('(' tokens ')' )?
        if (tokenIt.Skip("("))
        {
            var builder = new StringBuilder();
            var previousTokenKind = CppTokenKind.Punctuation;
            while (tokenIt.PeekText() != ")" && tokenIt.Next(out token))
            {
                if (token.Kind.IsIdentifierOrKeyword() && previousTokenKind.IsIdentifierOrKeyword())
                {
                    builder.Append(' ');
                }
                previousTokenKind = token.Kind;
                builder.Append(token.Text);
            }

            if (!tokenIt.Skip(")"))
            {
                return false;
            }
            arguments = builder.ToString();
        }

        var isVariadic = tokenIt.Skip("...");

        var previousToken = tokenIt.PreviousToken();

        attribute = new CppAttribute(tokenIt.Cursor, tokenIdentifier, AttributeKind.TokenAttribute)
        {
            Span = new CppSourceSpan(firstToken.Span.Start, previousToken.Span.End),
            Scope = scope,
            Arguments = arguments,
            IsVariadic = isVariadic,
        };
        return true;
    }

    #endregion "Private Functions"
}
