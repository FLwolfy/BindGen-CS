using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BGCS.Contracts.Native;
using BGCS.Frontend.Clang.Model;
using BGCS.Frontend.Clang.Model.Declarations;
using BGCS.Frontend.Clang.Model.Interfaces;
using BGCS.Frontend.Clang.Model.Metadata;
using ClangSharp.Interop;

namespace BGCS.Frontend.Clang.Parsing;

/// <summary>Distinguishes genuine direct-call-only compiler intrinsics from ordinary addressable builtins.</summary>
internal static class NativeCallableAddressabilityFactReader
{
    internal static unsafe void Read(
        CXIndex index,
        string[] arguments,
        string headers,
        CppCompilation compilation
    ) {
        CppFunction[] candidates = Functions(compilation).Where(function => function.cursor.Kind == CXCursorKind.CXCursor_FunctionDecl
            && function.parent is not CppClass && function.attributes.Any(attribute => attribute.name.Equals("builtin", StringComparison.OrdinalIgnoreCase))
            && Regex.IsMatch(function.name, "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant))
            .Distinct().ToArray();
        if (candidates.Length == 0) return;
        string stem = "bgcs.compiler.address-probe." + Guid.NewGuid().ToString("N") + ".";
        StringBuilder source = new(headers);
        for (int number = 0; number < candidates.Length; number++)
        {
            CppFunction candidate = candidates[number];
            string qualifiedName = string.IsNullOrEmpty(candidate.fullParentName) ? candidate.name : candidate.fullParentName + "::" + candidate.name;
            source.Append("\n#line 1 \"").Append(stem).Append(number.ToString(CultureInfo.InvariantCulture)).AppendLine("\"")
                .Append("void bgcs_address_probe_").Append(number.ToString(CultureInfo.InvariantCulture)).Append("(void) { (void)&")
                .Append(qualifiedName).AppendLine("; }");
        }
        using CXUnsavedFile input = CXUnsavedFile.Create(CppParser.C_CPPASTROOTFILENAME, source.ToString());
        ReadOnlySpan<CXUnsavedFile> inputs = stackalloc CXUnsavedFile[] { input };
        using CXTranslationUnit probe = CXTranslationUnit.Parse(index, CppParser.C_CPPASTROOTFILENAME,
            arguments.Concat(new[] { "-ferror-limit=0" }).ToArray(), inputs, CXTranslationUnit_Flags.CXTranslationUnit_None);
        Dictionary<int, List<string>> errors = [];
        bool unrelatedFailure = false;
        for (uint diagnosticIndex = 0; diagnosticIndex < probe.NumDiagnostics; diagnosticIndex++)
        {
            using CXDiagnostic diagnostic = probe.GetDiagnostic(diagnosticIndex);
            if (diagnostic.Severity is not (CXDiagnosticSeverity.CXDiagnostic_Error or CXDiagnosticSeverity.CXDiagnostic_Fatal)) continue;
            diagnostic.Location.GetPresumedLocation(out CXString presumedFile, out _, out _);
            using (presumedFile)
            using (CXString spelling = diagnostic.Spelling)
            {
                string file = presumedFile.ToString();
                if (!file.StartsWith(stem, StringComparison.Ordinal) || !int.TryParse(file.AsSpan(stem.Length), out int number) || number < 0 || number >= candidates.Length)
                { unrelatedFailure = true; continue; }
                if (!errors.TryGetValue(number, out List<string>? messages)) errors.Add(number, messages = []);
                messages.Add(spelling.ToString());
            }
        }
        if (unrelatedFailure)
        {
            compilation.diagnostics.Error("The compiler could not reproduce the native input while checking builtin function addressability.");
            return;
        }
        for (int number = 0; number < candidates.Length; number++)
        {
            if (!errors.TryGetValue(number, out List<string>? messages))
                compilation.callableAddressabilityFacts[candidates[number]] = NativeCallableAddressability.Addressable;
            else if (messages.All(message => message == "builtin functions must be directly called"))
                compilation.callableAddressabilityFacts[candidates[number]] = NativeCallableAddressability.DirectCallOnly;
            else
                compilation.diagnostics.Error($"Could not establish native builtin addressability for '{candidates[number].name}': {string.Join("; ", messages)}", candidates[number].span.start);
        }
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
}
