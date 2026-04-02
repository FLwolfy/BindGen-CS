using System;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.

using ClangSharp.Interop;

namespace BGCS.CppAst.Utilities;
public unsafe class AttributeTokenizer : Tokenizer
{
    public AttributeTokenizer(CXCursor cursor) : base(cursor)
    {
    }

    public AttributeTokenizer(CXTranslationUnit tu, CXSourceRange range) : base(tu, range)
    {
    }

    private uint IncOffset(int inc, uint offset)
    {
        if (inc >= 0)
            offset += (uint)inc;
        else
            offset -= (uint)-inc;
        return offset;
    }

    private Tuple<CXSourceRange, CXSourceRange> GetExtent(CXTranslationUnit tu, CXCursor cur)
    {
        var cursorExtend = cur.Extent;
        var begin = cursorExtend.Start;
        var end = cursorExtend.End;

        bool CursorIsFunction(CXCursorKind inKind)
        {
            return inKind == CXCursorKind.CXCursor_FunctionDecl || inKind == CXCursorKind.CXCursor_CXXMethod
                   || inKind == CXCursorKind.CXCursor_Constructor || inKind == CXCursorKind.CXCursor_Destructor
                   || inKind == CXCursorKind.CXCursor_ConversionFunction;
        }

        bool CursorIsVar(CXCursorKind inKind)
        {
            return inKind == CXCursorKind.CXCursor_VarDecl || inKind == CXCursorKind.CXCursor_FieldDecl;
        }

        bool IsInRange(CXSourceLocation loc, CXSourceRange range)
        {
            var xbegin = range.Start;
            var xend = range.End;

            loc.GetSpellingLocation(out var fileLocation, out var lineLocation, out var u1, out var u2);
            xbegin.GetSpellingLocation(out var fileBegin, out var lineBegin, out u1, out u2);
            xend.GetSpellingLocation(out var fileEnd, out var lineEnd, out u1, out u2);

            return lineLocation >= lineBegin && lineLocation < lineEnd && fileLocation.Equals(fileBegin);
        }

        bool HasInlineTypeDefinition(CXCursor varDecl)
        {
            var typeDecl = varDecl.Type.Declaration;
            if (typeDecl.IsNull)
                return false;

            var typeLocation = typeDecl.Location;
            var varRange = typeDecl.Extent;
            return IsInRange(typeLocation, varRange);
        }

        CXSourceLocation GetNextLocation(CXSourceLocation loc, int inc = 1)
        {
            CXSourceLocation value;
            loc.GetSpellingLocation(out var file, out var line, out var column, out var originalOffset);
            var signedOffset = (int)column + inc;
            var shouldUseLine = column != 0 && signedOffset > 0;
            if (shouldUseLine)
            {
                value = tu.GetLocation(file, line, (uint)signedOffset);
            }
            else
            {
                var offset = IncOffset(inc, originalOffset);
                value = tu.GetLocationForOffset(file, offset);
            }

            return value;
        }

        CXSourceLocation GetPrevLocation(CXSourceLocation loc, int tokenLength)
        {
            var inc = 1;
            while (true)
            {
                var locBefore = GetNextLocation(loc, -inc);
                CXToken* tokens;
                uint size;
                clang.tokenize(tu, clang.getRange(locBefore, loc), &tokens, &size);
                if (size == 0)
                    return CXSourceLocation.Null;

                var tokenLocation = tokens[0].GetLocation(tu);
                if (locBefore.Equals(tokenLocation))
                {
                    return GetNextLocation(loc, -1 * (inc + tokenLength - 1));
                }
                else
                    ++inc;
            }
        }

        bool TokenIsBefore(CXSourceLocation loc, string tokenString)
        {
            var length = tokenString.Length;
            var locBefore = GetPrevLocation(loc, length);

            var tokenizer = new Tokenizer(tu, clang.getRange(locBefore, loc));
            if (tokenizer.Count == 0) return false;

            return tokenizer.GetStringForLength(length) == tokenString;
        }

        bool TokenAtIs(CXSourceLocation loc, string tokenString)
        {
            var length = tokenString.Length;

            var locAfter = GetNextLocation(loc, length);
            var tokenizer = new Tokenizer(tu, clang.getRange(locAfter, loc));

            return tokenizer.GetStringForLength(length) == tokenString;
        }

        bool ConsumeIfTokenAtIs(ref CXSourceLocation loc, string tokenString)
        {
            var length = tokenString.Length;

            var locAfter = GetNextLocation(loc, length);
            var tokenizer = new Tokenizer(tu, clang.getRange(locAfter, loc));
            if (tokenizer.Count == 0)
                return false;

            if (tokenizer.GetStringForLength(length) == tokenString)
            {
                loc = locAfter;
                return true;
            }
            else
                return false;
        }

        bool ConsumeIfTokenBeforeIs(ref CXSourceLocation loc, string tokenString)
        {
            var length = tokenString.Length;

            var locBefore = GetPrevLocation(loc, length);

            var tokenizer = new Tokenizer(tu, clang.getRange(locBefore, loc));
            if (tokenizer.GetStringForLength(length) == tokenString)
            {
                loc = locBefore;
                return true;
            }
            else
                return false;
        }

        bool CheckIfValidOrReset(ref CXSourceLocation checkedLocation, CXSourceLocation resetLocation)
        {
            bool isValid = true;
            if (checkedLocation.Equals(CXSourceLocation.Null))
            {
                checkedLocation = resetLocation;
                isValid = false;
            }

            return isValid;
        }

        var kind = cur.Kind;
        if (CursorIsFunction(kind) || CursorIsFunction(cur.TemplateCursorKind)
        || kind == CXCursorKind.CXCursor_VarDecl || kind == CXCursorKind.CXCursor_FieldDecl || kind == CXCursorKind.CXCursor_ParmDecl
        || kind == CXCursorKind.CXCursor_NonTypeTemplateParameter)
        {
            while (TokenIsBefore(begin, "]]") || TokenIsBefore(begin, ")"))
            {
                var saveBegin = begin;
                if (ConsumeIfTokenBeforeIs(ref begin, "]]"))
                {
                    bool isValid = true;
                    while (!ConsumeIfTokenBeforeIs(ref begin, "[[") && isValid)
                    {
                        begin = GetPrevLocation(begin, 1);
                        isValid = CheckIfValidOrReset(ref begin, saveBegin);
                    }

                    if (!isValid)
                    {
                        break;
                    }
                }
                else if (ConsumeIfTokenBeforeIs(ref begin, ")"))
                {
                    var parenCount = 1;
                    for (var lastBegin = begin; parenCount != 0; lastBegin = begin)
                    {
                        if (TokenIsBefore(begin, "("))
                            --parenCount;
                        else if (TokenIsBefore(begin, ")"))
                            ++parenCount;

                        begin = GetPrevLocation(begin, 1);

                        // We have reached the end of the source of trying to deal
                        // with the potential of alignas, so we just break, which
                        // will cause ConsumeIfTokenBeforeIs(ref begin, "alignas") to be false
                        // and thus fall back to saveBegin which is the correct behavior
                        if (!CheckIfValidOrReset(ref begin, saveBegin))
                            break;
                    }

                    if (!ConsumeIfTokenBeforeIs(ref begin, "alignas"))
                    {
                        begin = saveBegin;
                        break;
                    }
                }
            }

            if (CursorIsVar(kind) || CursorIsVar(cur.TemplateCursorKind))
            {
                if (HasInlineTypeDefinition(cur))
                {
                    var typeCursor = clang.getTypeDeclaration(clang.getCursorType(cur));
                    var typeExtent = clang.getCursorExtent(typeCursor);

                    var typeBegin = clang.getRangeStart(typeExtent);
                    var typeEnd = clang.getRangeEnd(typeExtent);

                    return new Tuple<CXSourceRange, CXSourceRange>(clang.getRange(begin, typeBegin), clang.getRange(typeEnd, end));
                }
            }
            else if (kind == CXCursorKind.CXCursor_TemplateTypeParameter && TokenAtIs(end, "("))
            {
                var next = GetNextLocation(end, 1);
                var prev = end;
                for (var parenCount = 1; parenCount != 0; next = GetNextLocation(next, 1))
                {
                    if (TokenAtIs(next, "("))
                        ++parenCount;
                    else if (TokenAtIs(next, ")"))
                        --parenCount;
                    prev = next;
                }
                end = next;
            }
            else if (kind == CXCursorKind.CXCursor_TemplateTemplateParameter && TokenAtIs(end, "<"))
            {
                var next = GetNextLocation(end, 1);
                for (var angleCount = 1; angleCount != 0; next = GetNextLocation(next, 1))
                {
                    if (TokenAtIs(next, ">"))
                        --angleCount;
                    else if (TokenAtIs(next, ">>"))
                        angleCount -= 2;
                    else if (TokenAtIs(next, "<"))
                        ++angleCount;
                }

                while (!TokenAtIs(next, ">") && !TokenAtIs(next, ","))
                    next = GetNextLocation(next, 1);

                end = GetPrevLocation(next, 1);
            }
            else if (kind == CXCursorKind.CXCursor_TemplateTypeParameter || kind == CXCursorKind.CXCursor_NonTypeTemplateParameter
                || kind == CXCursorKind.CXCursor_TemplateTemplateParameter)
            {
                ConsumeIfTokenAtIs(ref end, "...");
            }
            else if (kind == CXCursorKind.CXCursor_EnumConstantDecl && !TokenAtIs(end, ","))
            {
                var parent = clang.getCursorLexicalParent(cur);
                end = clang.getRangeEnd(clang.getCursorExtent(parent));
            }
        }

        return new Tuple<CXSourceRange, CXSourceRange>(clang.getRange(begin, end), clang.getNullRange());
    }

    public override CXSourceRange GetRange(CXCursor cursor)
    {
        /*  This process is complicated when parsing attributes that use
            C++11 syntax, essentially even if libClang understands them
            it doesn't always return them back as parse of the token range.

            This is kind of frustrating when you want to be able to do something
            with custom or even compiler attributes in your parsing. Thus we have
            to do things a little manually in order to make this work.

            This code supports stepping back when its valid to parse attributes, it
            doesn't currently support all cases but it supports most valid cases.
        */
        var range = GetExtent(tu, cursor);

        var beg = range.Item1.Start;
        var end = range.Item1.End;
        if (!range.Item2.Equals(CXSourceRange.Null))
            end = range.Item2.End;

        return clang.getRange(beg, end);
    }
}
