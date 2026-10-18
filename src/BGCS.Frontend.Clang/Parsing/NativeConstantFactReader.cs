using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.Frontend.Clang.Model;
using BGCS.Frontend.Clang.Model.Declarations;
using BGCS.Frontend.Clang.Model.Interfaces;
using BGCS.Frontend.Clang.Model.Metadata;
using BGCS.Frontend.Clang.Utilities;
using ClangSharp.Interop;

namespace BGCS.Frontend.Clang.Parsing;

internal static class NativeConstantFactReader
{
    internal static void ReadFields(
        CppCompilation compilation,
        IReadOnlyDictionary<string, long> literalExtents
    ) {
        Dictionary<string, CppFunction> functions = FunctionIdentities(compilation);
        foreach (CppField field in Fields(compilation))
        {
            if (field.cursor.Kind != CXCursorKind.CXCursor_VarDecl || !NativeStringConstantFactReader.IsImmutable(field.cursor.Type)) continue;
            NativeScalarConstant? value = Evaluate(field.cursor, field.fullParentName + "::" + field.name);
            if (value is not null) compilation.constantFacts[field] = value;
            else if (NativeStringConstantFactReader.Read(field.cursor, field.fullParentName + "::" + field.name, literalExtents) is { } text)
                compilation.stringConstantFacts[field] = text;
            else if (ReferencedFunction(field.cursor, functions) is { } function)
                compilation.functionReferenceFacts[field] = function;
        }
    }

    internal static unsafe void ReadMacros(
        CXIndex index,
        string[] arguments,
        string source,
        CppCompilation compilation,
        SourceLanguage language,
        bool includeSystemDeclarations,
        IReadOnlyDictionary<string, long> literalExtents
    ) {
        CppMacro[] definitions = compilation.macros.Where(macro => includeSystemDeclarations || !macro.cursor.Location.IsInSystemHeader).ToArray();
        if (definitions.Length == 0) return;
        CppMacro[] current = definitions.GroupBy(macro => macro.name, StringComparer.Ordinal).Select(group => group.Last()).ToArray();
        foreach (CppMacro definition in definitions) compilation.macroActiveFacts[definition] = false;
        Dictionary<string, MacroDependencies> dependencies = ReadMacroDependencies(compilation);
        Dictionary<string, CppFunction> functions = FunctionIdentities(compilation);
        string stem = "bgcs_constant_probe_" + Guid.NewGuid().ToString("N");
        StringBuilder probeSource = new(source);
        // Place every active-definition marker before any potentially invalid
        // replacement. Expression recovery cannot falsify later macro activity.
        for (int candidate = 0; candidate < current.Length; candidate++)
        {
            CppMacro macro = current[candidate];
            if (!Regex.IsMatch(macro.name, "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)) continue;
            probeSource.Append("\n#if defined(").Append(macro.name).AppendLine(")");
            probeSource.Append("enum { ").Append(stem).Append("_active_").Append(candidate).AppendLine(" = 1 };");
            probeSource.AppendLine("#endif");
        }
        for (int candidate = 0; candidate < current.Length; candidate++)
        {
            CppMacro macro = current[candidate];
            if (!Regex.IsMatch(macro.name, "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)) continue;
            probeSource.Append("\n#if defined(").Append(macro.name).AppendLine(")");
            if (macro.parameters is null && !string.IsNullOrWhiteSpace(macro.value) && !DependsOnContext(macro.name, dependencies, []))
                probeSource.Append(language == SourceLanguage.Cpp ? "const auto " : "const __auto_type ")
                    .Append(stem).Append("_value_").Append(candidate).Append(" = (").Append(macro.name).AppendLine(");");
            probeSource.AppendLine("#endif");
        }
        using CXUnsavedFile input = CXUnsavedFile.Create(CppParser.C_CPPASTROOTFILENAME, probeSource.ToString());
        ReadOnlySpan<CXUnsavedFile> inputs = stackalloc CXUnsavedFile[] { input };
        // Invalid/non-expression macros are isolated in this probe. Their errors
        // never turn a valid input translation unit into a fabricated value.
        using CXTranslationUnit probe = CXTranslationUnit.Parse(index, CppParser.C_CPPASTROOTFILENAME,
            [.. arguments, "-ferror-limit=0"], inputs, CXTranslationUnit_Flags.CXTranslationUnit_None);
        HashSet<int> invalidLines = [];
        for (uint diagnosticIndex = 0; diagnosticIndex < probe.NumDiagnostics; diagnosticIndex++)
        {
            using CXDiagnostic diagnostic = probe.GetDiagnostic(diagnosticIndex);
            if (diagnostic.Severity is not (CXDiagnosticSeverity.CXDiagnostic_Error or CXDiagnosticSeverity.CXDiagnostic_Fatal)) continue;
            diagnostic.Location.GetExpansionLocation(out _, out uint line, out _, out _);
            invalidLines.Add(checked((int)line));
        }
        probe.Cursor.VisitChildren((
            cursor,
            _,
            _
        ) => {
            string name = CXUtil.GetCursorSpelling(cursor);
            string prefix = stem + "_active_";
            if (cursor.Kind == CXCursorKind.CXCursor_EnumDecl)
            {
                cursor.VisitChildren((
                    item,
                    _,
                    _
                ) => {
                    string itemName = CXUtil.GetCursorSpelling(item);
                    if (item.Kind == CXCursorKind.CXCursor_EnumConstantDecl && itemName.StartsWith(prefix, StringComparison.Ordinal)
                        && int.TryParse(itemName.AsSpan(prefix.Length), out int candidate) && candidate >= 0 && candidate < current.Length
                        && item.EnumConstantDeclValue == 1) compilation.macroActiveFacts[current[candidate]] = true;
                    return CXChildVisitResult.CXChildVisit_Continue;
                }, default);
            }
            prefix = stem + "_value_";
            if (cursor.Kind == CXCursorKind.CXCursor_VarDecl && name.StartsWith(prefix, StringComparison.Ordinal)
                && int.TryParse(name.AsSpan(prefix.Length), out int valueCandidate) && valueCandidate >= 0 && valueCandidate < current.Length)
            {
                cursor.Location.GetExpansionLocation(out _, out uint line, out _, out _);
                if (!invalidLines.Contains(checked((int)line)))
                {
                    if (Evaluate(cursor, current[valueCandidate].name) is { } constant)
                        compilation.constantFacts[current[valueCandidate]] = constant;
                    else if (NativeStringConstantFactReader.Read(cursor, current[valueCandidate].name, literalExtents) is { } text)
                        compilation.stringConstantFacts[current[valueCandidate]] = text;
                    else if (ReferencedFunction(cursor, functions) is { } referenced)
                        compilation.macroFunctionAliases[current[valueCandidate]] = referenced;
                }
            }
            return CXChildVisitResult.CXChildVisit_Continue;
        }, default);

    }

    private static Dictionary<string, CppFunction> FunctionIdentities(CppCompilation compilation) => Functions(compilation)
        .Where(function => function.cursor.Kind == CXCursorKind.CXCursor_FunctionDecl)
        .GroupBy(function => CXUtil.GetCursorUsrString(function.cursor), StringComparer.Ordinal).Where(group => group.Key.Length != 0)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

    private static unsafe CppFunction? ReferencedFunction(
        CXCursor expression,
        Dictionary<string, CppFunction> functions
    ) {
        if (expression.Kind == CXCursorKind.CXCursor_VarDecl) return ReferencedFunction(expression.InitExpr, functions);
        if (expression.Kind == CXCursorKind.CXCursor_DeclRefExpr)
        {
            CXCursor referenced = expression.Referenced;
            return referenced.Kind == CXCursorKind.CXCursor_FunctionDecl
                && functions.TryGetValue(CXUtil.GetCursorUsrString(referenced), out CppFunction? function) ? function : null;
        }
        if (expression.Kind is not (CXCursorKind.CXCursor_UnexposedExpr or CXCursorKind.CXCursor_ParenExpr)) return null;
        List<CXCursor> children = [];
        expression.VisitChildren((
            child,
            _,
            _
        ) => { children.Add(child); return CXChildVisitResult.CXChildVisit_Continue; }, default);
        return children.Count == 1 ? ReferencedFunction(children[0], functions) : null;
    }

    private sealed record MacroDependencies(
        ImmutableArray<string> names,
        bool invalidExpression
    );

    private static unsafe Dictionary<string, MacroDependencies> ReadMacroDependencies(CppCompilation compilation)
    {
        Dictionary<string, MacroDependencies> dependencies = new(StringComparer.Ordinal);
        // Dependencies include filtered SDK and command-line definitions. They
        // remain private compiler facts rather than public API declarations.
        // Union redefinitions conservatively: pragma push/pop can restore an
        // earlier context-dependent body without another definition cursor.
        compilation.translationUnit.Cursor.VisitChildren((
            cursor,
            _,
            _
        ) => {
            if (cursor.Kind != CXCursorKind.CXCursor_MacroDefinition) return CXChildVisitResult.CXChildVisit_Continue;
            string name = CXUtil.GetCursorSpelling(cursor);
            var tokens = compilation.translationUnit.Tokenize(cursor.Extent);
            try
            {
                HashSet<string> names = [];
                int parentheses = 0, brackets = 0;
                bool invalid = false;
                for (int index = 1; index < tokens.Length; index++)
                {
                    CXToken token = tokens[index];
                    string value = CXUtil.GetTokenSpelling(token, compilation.translationUnit);
                    if (token.Kind is CXTokenKind.CXToken_Identifier or CXTokenKind.CXToken_Keyword) names.Add(value);
                    if (token.Kind != CXTokenKind.CXToken_Punctuation) continue;
                    switch (value)
                    {
                        case "(": parentheses++; break;
                        case ")": if (--parentheses < 0) invalid = true; break;
                        case "[": brackets++; break;
                        case "]": if (--brackets < 0) invalid = true; break;
                        case "{" or "}" or ";" or "#" or "##": invalid = true; break;
                    }
                }
                invalid |= parentheses != 0 || brackets != 0;
                if (dependencies.TryGetValue(name, out MacroDependencies? previous))
                {
                    names.UnionWith(previous.names);
                    invalid |= previous.invalidExpression;
                }
                dependencies[name] = new(names.OrderBy(value => value, StringComparer.Ordinal).ToImmutableArray(), invalid);
            }
            finally { compilation.translationUnit.DisposeTokens(tokens); }
            return CXChildVisitResult.CXChildVisit_Continue;
        }, default);
        return dependencies;
    }

    private static bool DependsOnContext(
        string name,
        Dictionary<string, MacroDependencies> definitions,
        HashSet<string> seen
    ) {
        if (name is "__COUNTER__" or "__LINE__" or "__FILE__" or "__FILE_NAME__" or "__BASE_FILE__" or "__INCLUDE_LEVEL__"
            or "__DATE__" or "__TIME__" or "__TIMESTAMP__" or "__func__" or "__FUNCTION__" or "__FUNCSIG__" or "__FUNCDNAME__" or "__PRETTY_FUNCTION__"
            or "__builtin_FUNCTION" or "__builtin_FILE" or "__builtin_LINE" or "__builtin_COLUMN" or "__builtin_FUNCSIG"
            or "__has_include" or "__has_include_next" or "__has_builtin" or "__has_feature" or "__has_extension" or "__is_identifier"
            or "__has_attribute" or "__has_cpp_attribute" or "__has_c_attribute" or "__has_declspec_attribute" or "__has_warning"
            or "__builtin_has_attribute" or "_Pragma" or "__pragma" || name.StartsWith("__is_", StringComparison.Ordinal)) return true;
        if (!seen.Add(name) || !definitions.TryGetValue(name, out MacroDependencies? definition)) return false;
        return definition.invalidExpression || definition.names.Any(dependency => DependsOnContext(dependency, definitions, seen));
    }

    private static NativeScalarConstant? Evaluate(
        CXCursor cursor,
        string identity
    ) {
        CXType type = cursor.Type.CanonicalType;
        if (type.kind == CXTypeKind.CXType_Enum) type = type.Declaration.EnumDecl_IntegerType.CanonicalType;
        long size = type.SizeOf;
        if (size is not (1 or 2 or 4 or 8)) return null;
        using CXEvalResult result = cursor.Evaluate;
        Evidence evidence = new(EvidenceLevel.Declared, "clang.constant-evaluation:" + identity);
        if (result.Kind == CXEvalResultKind.CXEval_Int)
        {
            if (type.kind == CXTypeKind.CXType_Bool && result.AsLongLong is 0 or 1)
                return new(NativeScalarConstantKind.Boolean, result.AsLongLong.ToString(CultureInfo.InvariantCulture), checked((int)size), null, evidence);
            bool? signed = type.kind switch
            {
                CXTypeKind.CXType_Char_S or CXTypeKind.CXType_SChar or CXTypeKind.CXType_Short or CXTypeKind.CXType_Int
                    or CXTypeKind.CXType_Long or CXTypeKind.CXType_LongLong => true,
                CXTypeKind.CXType_Char_U or CXTypeKind.CXType_UChar or CXTypeKind.CXType_UShort or CXTypeKind.CXType_UInt
                    or CXTypeKind.CXType_ULong or CXTypeKind.CXType_ULongLong or CXTypeKind.CXType_Char16 or CXTypeKind.CXType_Char32 => false,
                _ => null
            };
            if (signed is null) return null;
            return new(NativeScalarConstantKind.Integer, signed.Value ? result.AsLongLong.ToString(CultureInfo.InvariantCulture)
                : result.AsUnsigned.ToString(CultureInfo.InvariantCulture), checked((int)size), signed, evidence);
        }
        if (result.Kind == CXEvalResultKind.CXEval_Float && type.kind == CXTypeKind.CXType_Float && size == 4)
            return new(NativeScalarConstantKind.Binary32, BitConverter.SingleToUInt32Bits((float)result.AsDouble).ToString("X8", CultureInfo.InvariantCulture), 4, null, evidence);
        if (result.Kind == CXEvalResultKind.CXEval_Float && type.kind == CXTypeKind.CXType_Double && size == 8)
            return new(NativeScalarConstantKind.Binary64, BitConverter.DoubleToUInt64Bits(result.AsDouble).ToString("X16", CultureInfo.InvariantCulture), 8, null, evidence);
        return null;
    }

    private static IEnumerable<CppField> Fields(ICppContainer container)
    {
        foreach (ICppDeclaration declaration in container.children)
        {
            if (declaration is CppField field) yield return field;
            if (declaration is ICppContainer nested) foreach (CppField child in Fields(nested)) yield return child;
        }
    }

    private static IEnumerable<CppFunction> Functions(ICppContainer container)
    {
        foreach (ICppDeclaration declaration in container.children)
        {
            if (declaration is CppFunction function) yield return function;
            if (declaration is ICppContainer nested) foreach (CppFunction child in Functions(nested)) yield return child;
        }
    }
}
