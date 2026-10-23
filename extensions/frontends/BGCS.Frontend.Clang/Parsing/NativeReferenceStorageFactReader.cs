using System;
using System.Linq;
using System.Text;
using BGCS.Frontend.Clang.Model.Metadata;
using BGCS.Frontend.Clang.Model.Types;
using BGCS.Frontend.Clang.Utilities;
using ClangSharp.Interop;

namespace BGCS.Frontend.Clang.Parsing;

/// <summary>Measures reference field carriers without confusing their storage with sizeof the referent.</summary>
internal static class NativeReferenceStorageFactReader
{
    internal static unsafe void Read(
        CXIndex index,
        string[] arguments,
        CppCompilation compilation
    ) {
        if (!compilation.storageFacts.Keys.Any(type => type is CppReferenceType)) return;
        string stem = "bgcs_reference_storage_" + Guid.NewGuid().ToString("N");
        string[] fields = ["char& slot;", "char&& slot;", "void (&slot)();", "void (&&slot)();"];
        StringBuilder source = new();
        for (int candidate = 0; candidate < fields.Length; candidate++)
            source.Append("struct ").Append(stem).Append('_').Append(candidate).Append(" { ").Append(fields[candidate]).AppendLine(" };");
        using CXUnsavedFile input = CXUnsavedFile.Create(CppParser.C_CPPASTROOTFILENAME, source.ToString());
        ReadOnlySpan<CXUnsavedFile> inputs = stackalloc CXUnsavedFile[] { input };
        using CXTranslationUnit probe = CXTranslationUnit.Parse(index, CppParser.C_CPPASTROOTFILENAME, arguments, inputs,
            CXTranslationUnit_Flags.CXTranslationUnit_SkipFunctionBodies);
        for (uint diagnosticIndex = 0; diagnosticIndex < probe.NumDiagnostics; diagnosticIndex++)
        {
            using CXDiagnostic diagnostic = probe.GetDiagnostic(diagnosticIndex);
            if (diagnostic.Severity is CXDiagnosticSeverity.CXDiagnostic_Error or CXDiagnosticSeverity.CXDiagnostic_Fatal)
                return; // Unavailable compiler facts remain unknown, never pointer-size substitutions.
        }
        probe.Cursor.VisitChildren((
            cursor,
            _,
            _
        ) => {
            string name = CXUtil.GetCursorSpelling(cursor);
            if (cursor.Kind != CXCursorKind.CXCursor_StructDecl || !name.StartsWith(stem + "_", StringComparison.Ordinal)
                || !int.TryParse(name.AsSpan(stem.Length + 1), out int candidate) || candidate < 0 || candidate >= fields.Length
                || cursor.Type.SizeOf <= 0 || cursor.Type.AlignOf <= 0) return CXChildVisitResult.CXChildVisit_Continue;
            bool valid = false;
            int members = 0;
            cursor.VisitChildren((
                field,
                _,
                _
            ) => {
                members++;
                CXType type = field.Type.CanonicalType;
                bool function = type.PointeeType.CanonicalType.kind is CXTypeKind.CXType_FunctionProto or CXTypeKind.CXType_FunctionNoProto;
                valid = field.Kind == CXCursorKind.CXCursor_FieldDecl && CXUtil.GetCursorSpelling(field) == "slot"
                    && type.kind == (candidate % 2 == 0 ? CXTypeKind.CXType_LValueReference : CXTypeKind.CXType_RValueReference)
                    && function == (candidate >= 2) && field.OffsetOfField == 0;
                return CXChildVisitResult.CXChildVisit_Continue;
            }, default);
            if (members == 1 && valid)
                compilation.referenceFieldStorageSizes[(candidate >= 2, candidate % 2 != 0)] = cursor.Type.SizeOf;
            return CXChildVisitResult.CXChildVisit_Continue;
        }, default);
    }
}
