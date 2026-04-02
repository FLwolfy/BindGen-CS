using System;
namespace BGCS.CppAst.Parsing.Visitors.MemberVisitors;
using ClangSharp.Interop;
using BGCS.CppAst.Collections;
using BGCS.CppAst.Model;
using BGCS.CppAst.Model.Expressions;
using BGCS.CppAst.Utilities;
using System.Collections.Generic;

public unsafe class MacroDefinitionVisitor : MemberVisitor
{
    public override IEnumerable<CXCursorKind> Kinds { get; } = [
        CXCursorKind.CXCursor_MacroDefinition
    ];

    protected override CppElement? VisitCore(CXCursor cursor, CXCursor parent)
    {
        // TODO: reuse internal class Tokenizer

        // As we don't have an API to check macros, we are
        var originalRange = cursor.Extent;
        var tu = cursor.TranslationUnit;

        // Try to extend the parsing of the macro to the end of line in order to recover comments
        originalRange.End.GetFileLocation(out var startFile, out var endLine, out var endColumn, out var startOffset);
        var range = originalRange;
        if (startFile.Handle != nint.Zero)
        {
            var nextLineLocation = clang.getLocation(tu, startFile, endLine + 1, 1);
            if (!nextLineLocation.Equals(CXSourceLocation.Null))
            {
                range = clang.getRange(originalRange.Start, nextLineLocation);
            }
        }

        var tokens = tu.Tokenize(range);

        var name = CXUtil.GetCursorSpelling(cursor);
        if (name.StartsWith("__cppast"))
        {
            //cppast system macros, just ignore here
            tu.DisposeTokens(tokens);
            return null;
        }

        CppMacro cppMacro = new(cursor, name);

        uint previousLine = 0;
        uint previousColumn = 0;
        bool parsingMacroParameters = false;
        List<string>? macroParameters = null;

        // Loop decoding tokens for the value
        // We need to parse
        for (int i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            var tokenRange = token.GetExtent(tu);
            tokenRange.Start.GetFileLocation(out var file, out var line, out var column, out var offset);
            if (line >= endLine + 1)
            {
                break;
            }
            var tokenStr = CXUtil.GetTokenSpelling(token, tu);

            // If we are parsing the token right after the MACRO name token
            // if the `(` is right after the name without
            if (i == 1 && tokenStr == "(" && previousLine == line && previousColumn == column)
            {
                parsingMacroParameters = true;
                macroParameters = [];
            }

            tokenRange.End.GetFileLocation(out file, out previousLine, out previousColumn, out offset);

            if (parsingMacroParameters)
            {
                if (tokenStr == ")")
                {
                    parsingMacroParameters = false;
                }
                else if (token.Kind != CXTokenKind.CXToken_Punctuation)
                {
                    macroParameters.Add(tokenStr);
                }
            }
            else if (i > 0)
            {
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
                        RootCompilation.Diagnostics.Warning($"Token kind {tokenStr} is not supported for macros", token.GetLocation(tu).ToSourceLocation());
                        break;
                }

                CppToken cppToken = new(cursor, cppTokenKind, tokenStr)
                {
                    Span = tokenRange.ToSourceRange()
                };

                cppMacro.Tokens.Add(cppToken);
            }
        }

        // Update the value from the tokens
        cppMacro.UpdateValueFromTokens();
        cppMacro.Parameters = macroParameters;

        var globalContainer = (CppGlobalDeclarationContainer)CurrentRootContainer.DeclarationContainer;
        globalContainer.Macros.Add(cppMacro);

        tu.DisposeTokens(tokens);
        return cppMacro;
    }
}
