using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using BGCS.Contracts.Common;
using BGCS.Contracts.Native;
using BGCS.Frontend.Clang.Utilities;
using ClangSharp.Interop;

namespace BGCS.Frontend.Clang.Parsing;

/// <summary>Copies bounded raw literal units before native cursor release, without CString truncation or source-text unescaping.</summary>
internal static class NativeStringConstantFactReader
{
    internal static unsafe IReadOnlyDictionary<string, long> ReadLiteralExtents(
        CXIndex index,
        string[] arguments,
        CXTranslationUnit input
    ) {
        // Sema rewrites an array initializer's StringLiteral.Type to the
        // destination array type. That size cannot bound getBytes(): a padded
        // destination has more elements than the independently stored literal.
        // Re-evaluate each exact token sequence outside an array initializer in
        // one compiler probe, retaining the same target and language arguments.
        HashSet<string> spellings = new(StringComparer.Ordinal);
        input.Cursor.VisitChildren((
            cursor,
            _,
            _
        ) => {
            if (cursor.Kind == CXCursorKind.CXCursor_StringLiteral && Tokens(cursor) is { Length: > 0 } tokens)
                spellings.Add(string.Join(" ", tokens));
            return CXChildVisitResult.CXChildVisit_Recurse;
        }, default);
        if (spellings.Count == 0) return new Dictionary<string, long>(StringComparer.Ordinal);
        string[] values = spellings.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        string stem = "bgcs_literal_extent_" + Guid.NewGuid().ToString("N") + "_";
        StringBuilder source = new();
        for (int item = 0; item < values.Length; item++)
            source.Append("enum { ").Append(stem).Append(item).Append(" = sizeof((").Append(values[item])
                .Append(")) / sizeof(((").Append(values[item]).AppendLine("))[0]) };");
        using CXUnsavedFile file = CXUnsavedFile.Create(CppParser.C_CPPASTROOTFILENAME, source.ToString());
        ReadOnlySpan<CXUnsavedFile> files = stackalloc CXUnsavedFile[] { file };
        using CXTranslationUnit probe = CXTranslationUnit.Parse(index, CppParser.C_CPPASTROOTFILENAME,
            arguments, files, CXTranslationUnit_Flags.CXTranslationUnit_None);
        for (uint item = 0; item < probe.NumDiagnostics; item++)
        {
            using CXDiagnostic diagnostic = probe.GetDiagnostic(item);
            if (diagnostic.Severity is CXDiagnosticSeverity.CXDiagnostic_Error or CXDiagnosticSeverity.CXDiagnostic_Fatal)
                return new Dictionary<string, long>(StringComparer.Ordinal);
        }
        Dictionary<string, long> result = new(StringComparer.Ordinal);
        probe.Cursor.VisitChildren((
            cursor,
            _,
            _
        ) => {
            string name = CXUtil.GetCursorSpelling(cursor);
            if (cursor.Kind == CXCursorKind.CXCursor_EnumConstantDecl && name.StartsWith(stem, StringComparison.Ordinal)
                && int.TryParse(name.AsSpan(stem.Length), out int item) && item >= 0 && item < values.Length)
                result[values[item]] = cursor.EnumConstantDeclValue;
            return CXChildVisitResult.CXChildVisit_Recurse;
        }, default);
        return result;
    }

    internal static bool IsImmutable(CXType type)
    {
        // Canonical array elements may discard the original local qualifiers.
        // Follow source array/typedef views before testing their element constness.
        HashSet<CXType> visited = [];
        while (visited.Add(type))
        {
            if (type.IsVolatileQualified) return false;
            if (type.kind is CXTypeKind.CXType_ConstantArray or CXTypeKind.CXType_IncompleteArray) type = type.ArrayElementType;
            else if (type.IsConstQualified) return true;
            else if (type.kind == CXTypeKind.CXType_Typedef) type = type.Declaration.TypedefDeclUnderlyingType;
            else if (type.kind == CXTypeKind.CXType_Elaborated) type = type.NamedType;
            else if (type.kind == CXTypeKind.CXType_Attributed) type = type.ModifiedType;
            else if (type.kind == CXTypeKind.CXType_Auto) type = type.DeducedType;
            else return false;
        }
        return false;
    }

    internal static unsafe NativeStringConstant? Read(
        CXCursor source,
        string identity,
        IReadOnlyDictionary<string, long> literalExtents
    ) {
        long? declaredArrayExtent = null;
        CXCursor literal = Literal(source, [], 0, ref declaredArrayExtent);
        if (literal.Kind != CXCursorKind.CXCursor_StringLiteral || literal.Type.CanonicalType.kind != CXTypeKind.CXType_ConstantArray) return null;
        CXType array = literal.Type.CanonicalType;
        long length = array.ArraySize, width = array.ArrayElementType.SizeOf;
        ImmutableArray<string> spellings = Tokens(literal);
        if (declaredArrayExtent is not null)
        {
            if (spellings.IsEmpty || !literalExtents.TryGetValue(string.Join(" ", spellings), out long independentLength)) return null;
            length = independentLength;
        }
        if (length < 1 || length > 8 * 1024 * 1024 || width is not (1 or 2 or 4)
            || declaredArrayExtent is long storageLength && storageLength != length) return null;
        NativeStringLiteralKind? kind = literal.StringLiteralKind switch
        {
            CX_StringKind.CX_SLK_Ordinary => NativeStringLiteralKind.Ordinary,
            CX_StringKind.CX_SLK_UTF8 => NativeStringLiteralKind.Utf8,
            CX_StringKind.CX_SLK_UTF16 => NativeStringLiteralKind.Utf16,
            CX_StringKind.CX_SLK_UTF32 => NativeStringLiteralKind.Utf32,
            CX_StringKind.CX_SLK_Wide => NativeStringLiteralKind.Wide,
            _ => null
        };
        if (kind is null) return null;
        // Pinned libClangSharp 20.1.2.4 returns createDup(StringLiteral::getBytes()).
        // LLVM getCodeUnit reads 16/32-bit units in host storage order. The AST
        // array extent includes the terminator, while getBytes excludes it.
        // See sources/libClangSharp/ClangSharp.cpp and LLVM clang/AST/Expr.h.
        using CXString bytes = literal.StringLiteralValue;
        int byteLength = checked((int)((length - 1) * width));
        if (bytes.data == null && byteLength != 0) return null;
        ReadOnlySpan<byte> data = new(bytes.data, byteLength);
        ImmutableArray<uint>.Builder units = ImmutableArray.CreateBuilder<uint>(checked((int)length));
        for (int offset = 0; offset < data.Length; offset += checked((int)width))
            units.Add(width == 1 ? data[offset] : width == 2 ? BitConverter.ToUInt16(data.Slice(offset, 2)) : BitConverter.ToUInt32(data.Slice(offset, 4)));
        units.Add(0);
        return new(kind.Value, units.ToImmutable(), checked((int)width), new(EvidenceLevel.Declared, "clang.literal-code-units:" + identity))
        { literalTokens = spellings };
    }

    private static unsafe ImmutableArray<string> Tokens(CXCursor literal)
    {
        ImmutableArray<string>.Builder spellings = ImmutableArray.CreateBuilder<string>();
        var tokens = literal.TranslationUnit.Tokenize(literal.Extent);
        try
        {
            bool exact = tokens.Length != 0;
            for (int index = 0; index < tokens.Length; index++)
            {
                if (tokens[index].Kind == CXTokenKind.CXToken_Comment) continue;
                if (tokens[index].Kind != CXTokenKind.CXToken_Literal) { exact = false; break; }
                spellings.Add(CXUtil.GetTokenSpelling(tokens[index], literal.TranslationUnit));
            }
            if (!exact) spellings.Clear();
        }
        finally { literal.TranslationUnit.DisposeTokens(tokens); }
        return spellings.ToImmutable();
    }

    private static unsafe CXCursor Literal(
        CXCursor expression,
        HashSet<CXCursor> visited,
        int depth,
        ref long? declaredArrayExtent
    ) {
        if (depth > 64 || !visited.Add(expression)) return default;
        if (expression.Kind == CXCursorKind.CXCursor_StringLiteral) return expression;
        // Array declarations also expose bound/type-expression children. Read
        // the compiler's initializer edge instead of mistaking those children
        // for an ambiguous value expression.
        if (expression.Kind == CXCursorKind.CXCursor_VarDecl)
        {
            CXType declared = expression.Type.CanonicalType;
            if (declared.kind == CXTypeKind.CXType_ConstantArray)
            {
                if (declaredArrayExtent is long previous && previous != declared.ArraySize) return default;
                declaredArrayExtent = declared.ArraySize;
            }
            return Literal(expression.InitExpr, visited, depth + 1, ref declaredArrayExtent);
        }
        if (expression.Kind == CXCursorKind.CXCursor_DeclRefExpr)
        {
            CXCursor referenced = expression.Referenced;
            return referenced.Kind == CXCursorKind.CXCursor_VarDecl && IsImmutable(referenced.Type) ? Literal(referenced, visited, depth + 1, ref declaredArrayExtent) : default;
        }
        if (expression.Kind is not (CXCursorKind.CXCursor_UnexposedExpr or CXCursorKind.CXCursor_ParenExpr)) return default;
        List<CXCursor> children = [];
        expression.VisitChildren((
            child,
            _,
            _
        ) => { children.Add(child); return CXChildVisitResult.CXChildVisit_Continue; }, default);
        return children.Count == 1 ? Literal(children[0], visited, depth + 1, ref declaredArrayExtent) : default;
    }
}
