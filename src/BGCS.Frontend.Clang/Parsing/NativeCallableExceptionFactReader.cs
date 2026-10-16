using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BGCS.Frontend.Clang.Model;
using BGCS.Frontend.Clang.Model.Declarations;
using BGCS.Frontend.Clang.Model.Interfaces;
using BGCS.Frontend.Clang.Model.Metadata;
using BGCS.Frontend.Clang.Model.Templates;
using BGCS.Frontend.Clang.Utilities;
using ClangSharp.Interop;

namespace BGCS.Frontend.Clang.Parsing;

/// <summary>Evaluates computed exception contracts with the same compiler configuration as the original parse.</summary>
internal static class NativeCallableExceptionFactReader
{
    internal static unsafe void Read(
        CXIndex index,
        string[] arguments,
        string headers,
        CppCompilation compilation
    ) {
        CppFunction[] candidates = Functions(compilation).Where(function => function.cursor.kind != 0
            && function.cursor.Type.ExceptionSpecificationType == CXCursor_ExceptionSpecificationKind.CXCursor_ExceptionSpecificationKind_ComputedNoexcept
            && IsBound(function)).Distinct().ToArray();
        if (candidates.Length == 0) return;
        string stem = "bgcs_exception_fact_" + Guid.NewGuid().ToString("N");
        StringBuilder source = new(headers);
        source.Append("\nnamespace ").Append(stem).AppendLine(" {")
            .AppendLine("template<class T> struct Contract;");
        foreach (string qualification in new[] { "", " const", " volatile", " const volatile" })
            foreach (string reference in new[] { "", " &", " &&" })
                foreach (bool variadic in new[] { false, true })
                    source.Append("template<bool NoThrow, class R, class... A> struct Contract<R(A...")
                        .Append(variadic ? ", ..." : string.Empty).Append(')').Append(qualification).Append(reference)
                        .AppendLine(" noexcept(NoThrow)> { static constexpr bool value = NoThrow; };");
        source.AppendLine("}").Append("enum ").Append(stem).AppendLine("_results {");
        for (int candidate = 0; candidate < candidates.Length; candidate++)
        {
            string type = CXUtil.GetTypeSpelling(candidates[candidate].cursor.Type.CanonicalType);
            source.Append(stem).Append('_').Append(candidate).Append(" = ").Append(stem).Append("::Contract<")
                .Append(type).AppendLine(">::value ? 1 : 0,");
        }
        source.AppendLine("};");
        using CXUnsavedFile input = CXUnsavedFile.Create(CppParser.C_CPPASTROOTFILENAME, source.ToString());
        ReadOnlySpan<CXUnsavedFile> inputs = stackalloc CXUnsavedFile[] { input };
        using CXTranslationUnit probe = CXTranslationUnit.Parse(index, CppParser.C_CPPASTROOTFILENAME, arguments, inputs,
            CXTranslationUnit_Flags.CXTranslationUnit_SkipFunctionBodies);
        for (uint diagnosticIndex = 0; diagnosticIndex < probe.NumDiagnostics; diagnosticIndex++)
        {
            using CXDiagnostic diagnostic = probe.GetDiagnostic(diagnosticIndex);
            if (diagnostic.Severity is CXDiagnosticSeverity.CXDiagnostic_Error or CXDiagnosticSeverity.CXDiagnostic_Fatal)
                return; // Unexpressible, inaccessible or pre-C++17 types remain unknown.
        }
        probe.Cursor.VisitChildren((
            cursor,
            _,
            _
        ) => {
            if (cursor.Kind != CXCursorKind.CXCursor_EnumDecl || CXUtil.GetCursorSpelling(cursor) != stem + "_results")
                return CXChildVisitResult.CXChildVisit_Continue;
            cursor.VisitChildren((
                value,
                _,
                _
            ) => {
                string name = CXUtil.GetCursorSpelling(value);
                if (value.Kind == CXCursorKind.CXCursor_EnumConstantDecl && name.StartsWith(stem + "_", StringComparison.Ordinal)
                    && int.TryParse(name.AsSpan(stem.Length + 1), out int candidate) && candidate >= 0 && candidate < candidates.Length
                    && value.EnumConstantDeclValue is 0 or 1)
                    compilation.callableExceptionFacts[candidates[candidate]] = value.EnumConstantDeclValue == 1;
                return CXChildVisitResult.CXChildVisit_Continue;
            }, default);
            return CXChildVisitResult.CXChildVisit_Continue;
        }, default);
    }

    private static IEnumerable<CppFunction> Functions(ICppContainer container)
    {
        foreach (ICppDeclaration declaration in container.children)
        {
            if (declaration is CppFunction function) yield return function;
            if (declaration is ICppContainer child)
                foreach (CppFunction nested in Functions(child)) yield return nested;
        }
    }

    internal static bool IsBound(CppFunction function)
    {
        for (CppElement? element = function; element is not null; element = element.parent as CppElement)
            if (element is CppFunction { isFunctionTemplate: true } || element is CppClass
                { templateKind: CppTemplateKind.TemplateClass or CppTemplateKind.PartialTemplateClass or CppTemplateKind.ObjCGenericClass }) return false;
        return true;
    }
}
