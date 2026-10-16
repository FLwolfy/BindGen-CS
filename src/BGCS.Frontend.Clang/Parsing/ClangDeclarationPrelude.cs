using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Requests;
using BGCS.Frontend.Clang.Diagnostics;
using BGCS.Frontend.Clang.Interop;
using BGCS.Frontend.Clang.Model.Metadata;
using BGCS.Frontend.Clang.Utilities;
using BGCS.Infrastructure.IO;
using ClangSharp.Interop;

namespace BGCS.Frontend.Clang.Parsing;

// Owns only a private PCH and detached dependency records. Its caller keeps this
// lifetime outside the final compilation, including all live fact contributors.
internal sealed class ClangDeclarationPrelude : IDisposable
{
    private readonly string m_root;
    internal string[] arguments { get; }
    internal string consumerPrefix { get; }
    internal ImmutableArray<InputDependency> inputs { get; }
    internal string[] ownedFiles { get; }
    internal List<CppDiagnosticMessage> diagnostics { get; } = [];

    private ClangDeclarationPrelude(
        string root,
        string prefix,
        ImmutableArray<InputDependency> inputs,
        string[] systemArguments
    ) {
        m_root = root;
        consumerPrefix = prefix;
        this.inputs = inputs;
        ownedFiles = [Path.Combine(root, "declaration-prelude.hpp"), Path.Combine(root, "declarations.pch")];
        arguments = [.. systemArguments, "-Xclang", "-include-pch", "-Xclang", ownedFiles[1]];
    }

    internal static unsafe ClangDeclarationPrelude? Prepare(
        NativeDeclarationPrelude? prelude,
        CppParserOptions options,
        IReadOnlyList<string> searchRoots,
        ImmutableArray<string> exclusions,
        CancellationToken cancellationToken
    ) {
        if (prelude is null) return null;
        if (options.parserKind != SourceLanguage.Cpp || prelude.headers.IsDefaultOrEmpty || string.IsNullOrWhiteSpace(prelude.source)
            || prelude.headers.Any(header => !Path.IsPathFullyQualified(header) || !File.Exists(header)))
            throw new ArgumentException("A declaration prelude requires initialized existing absolute C++ dependency headers.");
        ClangNativeRuntime.EnsureLoaded();
        string root = Path.Combine(PhysicalPathResolver.Resolve(Path.GetTempPath()), "bgcs-clang-prelude-" + Guid.NewGuid().ToString("N"));
        if (searchRoots.Any(search => Within(root, PhysicalPathResolver.Resolve(search)))
            && !exclusions.Any(exclusion => Within(root, PhysicalPathResolver.Resolve(exclusion))))
            throw new NotSupportedException("The private declaration prelude would change an observed include-search directory; declare an isolated parser workspace.");
        List<string> baseArguments = CppParser.CreateArguments(options);
        if (baseArguments.Any(argument => argument is "-include-pch" or "-include-pth" or "-include" or "-imacros"
            || argument.StartsWith("-fmodule-file", StringComparison.Ordinal)))
            throw new NotSupportedException("An isolated declaration prelude cannot be combined with another forced header, module or PCH.");
        baseArguments.RemoveAll(argument => argument is "-E" or "-dM" or "-xc++");
        baseArguments.Add("-xc++-header");
        // Absolute includes still name authenticated SDK entries. Apply the same
        // system search classification to both PCH construction and its consumer.
        string[] systemArguments = prelude.headers.Select(header => Path.GetDirectoryName(header)!).Distinct(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .SelectMany(directory => new[] { "-isystem", directory }).ToArray();
        baseArguments.AddRange(systemArguments);
        Directory.CreateDirectory(root);
        ClangDeclarationPrelude? result = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using CXIndex index = CXIndex.Create();
            List<CppDiagnosticMessage> messages = [];
            using CXTranslationUnit discovery = Parse(prelude.source);
            HashSet<string> names = new(DynamicBuiltins, StringComparer.Ordinal);
            Exception? callbackFailure = null;
            discovery.Cursor.VisitChildren((
                cursor,
                _,
                _
            ) => {
                try
                {
                    if (cursor.Kind == CXCursorKind.CXCursor_MacroDefinition) names.Add(CXUtil.GetCursorSpelling(cursor));
                }
                catch (Exception failure) { callbackFailure = failure; }
                return CXChildVisitResult.CXChildVisit_Continue;
            }, default);
            if (callbackFailure is not null) throw new IOException("The prelude macro environment could not be frozen.", callbackFailure);
            if (names.Any(name => name.Length == 0 || name.Any(character => character is '\r' or '\n' or '\0')))
                throw new IOException("A prelude macro has an invalid identifier extent.");
            ImmutableArray<InputDependency> first = ExternalInputs(discovery);
            StringBuilder cleanup = new("#pragma clang diagnostic push\n#pragma clang diagnostic ignored \"-Wbuiltin-macro-redefined\"\n");
            foreach (string name in names.Order(StringComparer.Ordinal)) cleanup.Append("#undef ").AppendLine(name);
            cleanup.AppendLine("#pragma clang diagnostic pop");
            string isolatedSource = RewriteEntries(discovery, prelude.source, rewrite: true);
            string clearedSource = isolatedSource + "\n" + cleanup;
            using CXTranslationUnit materialized = Parse(clearedSource);
            _ = RewriteEntries(materialized, clearedSource, rewrite: false);
            ImmutableArray<InputDependency> second = ExternalInputs(materialized);
            if (!first.SequenceEqual(second)) throw new IOException("Declaration prelude dependencies changed between discovery and materialization.");
            cancellationToken.ThrowIfCancellationRequested();
            CXSaveError saved = materialized.Save(Path.Combine(root, "declarations.pch"), materialized.DefaultSaveOptions);
            if ((int)saved != 0) throw new IOException("The declaration prelude could not be serialized: " + saved);
            // Dynamic builtin undef history is not retained reliably in a PCH.
            // Clear the exact environment again after loading, before user tokens.
            cleanup.Append("#if ").AppendJoin(" || ", names.Order(StringComparer.Ordinal).Select(name => "defined(" + name + ")"))
                .AppendLine("\n#error BGCS declaration prelude leaked a macro into frozen native source\n#endif");
            cleanup.AppendLine("#line 1 \"cppast.input\"");
            result = new(root, cleanup.ToString(), second, systemArguments);
            result.diagnostics.AddRange(messages);
            return result;

            string RewriteEntries(
                CXTranslationUnit unit,
                string source,
                bool rewrite
            ) {
                string sourcePath = Path.Combine(root, "declaration-prelude.hpp");
                List<(int start, int end, string text)> replacements = [];
                List<string> observed = [];
                Exception? failure = null;
                unit.Cursor.VisitChildren((
                    cursor,
                    _,
                    _
                ) => {
                    if (failure is not null || cursor.Kind != CXCursorKind.CXCursor_InclusionDirective) return CXChildVisitResult.CXChildVisit_Continue;
                    try
                    {
                        cursor.Extent.Start.GetFileLocation(out CXFile file, out _, out _, out uint start);
                        using CXString origin = file.Name;
                        if (!Path.GetFullPath(origin.ToString()).Equals(sourcePath, Comparison)) return CXChildVisitResult.CXChildVisit_Continue;
                        using CXString actual = cursor.IncludedFile.Name;
                        string header = Path.GetFullPath(actual.ToString());
                        observed.Add(header);
                        if (rewrite)
                        {
                            string name = Path.GetFileName(header);
                            if (name.Any(character => character is '<' or '>' or '\r' or '\n' or '\0'))
                                throw new NotSupportedException("An authenticated dependency has no unambiguous system include spelling.");
                            cursor.Extent.End.GetFileLocation(out _, out _, out _, out uint end);
                            replacements.Add((checked((int)start), checked((int)end), "#include <" + name + ">"));
                        }
                    }
                    catch (Exception exception) { failure = exception; }
                    return CXChildVisitResult.CXChildVisit_Continue;
                }, default);
                if (failure is not null) throw new IOException("The declaration prelude include entries could not be authenticated.", failure);
                if (observed.Count != prelude.headers.Length || observed.Where((
                    header,
                    position
                ) =>
                    !PhysicalPathResolver.Resolve(header).Equals(PhysicalPathResolver.Resolve(prelude.headers[position]), Comparison)).Any())
                    throw new IOException("The declaration prelude resolved a different ordered dependency entry; ambiguous include basenames are unsupported.");
                if (!rewrite) return source;
                byte[] original = Encoding.UTF8.GetBytes(source);
                using MemoryStream output = new();
                int copied = 0;
                foreach (var replacement in replacements.OrderBy(item => item.start))
                {
                    if (replacement.start < copied || replacement.end < replacement.start || replacement.end > original.Length)
                        throw new IOException("A declaration prelude include has an invalid compiler byte extent.");
                    output.Write(original.AsSpan(copied, replacement.start - copied));
                    output.Write(Encoding.UTF8.GetBytes(replacement.text));
                    copied = replacement.end;
                }
                output.Write(original.AsSpan(copied));
                return Encoding.UTF8.GetString(output.ToArray());
            }

            ImmutableArray<InputDependency> ExternalInputs(CXTranslationUnit unit)
                => IncludeDependencyCollector.Collect(unit).Where(input => !input.path.Equals(Path.Combine(root, "declaration-prelude.hpp"), Comparison)
                    && !input.path.Equals(Path.Combine(root, "declarations.pch"), Comparison)).ToImmutableArray();

            CXTranslationUnit Parse(string source)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string sourcePath = Path.Combine(root, "declaration-prelude.hpp");
                File.WriteAllText(sourcePath, source, new UTF8Encoding(false));
                CXTranslationUnit unit = CXTranslationUnit.Parse(index, sourcePath, baseArguments.ToArray(), [],
                    CXTranslationUnit_Flags.CXTranslationUnit_DetailedPreprocessingRecord | CXTranslationUnit_Flags.CXTranslationUnit_ForSerialization
                    | CXTranslationUnit_Flags.CXTranslationUnit_IncludeAttributedTypes | CXTranslationUnit_Flags.CXTranslationUnit_VisitImplicitAttributes);
                try
                {
                    bool errors = false;
                    for (uint offset = 0; offset < unit.NumDiagnostics; offset++)
                    {
                        using CXDiagnostic diagnostic = unit.GetDiagnostic(offset);
                        string text = CppParser.GetMessageAndLocation(source, diagnostic, out var location, options.usePresumedSourceLocations);
                        CppLogMessageType level = diagnostic.Severity switch
                        {
                            CXDiagnosticSeverity.CXDiagnostic_Error or CXDiagnosticSeverity.CXDiagnostic_Fatal => CppLogMessageType.Error,
                            CXDiagnosticSeverity.CXDiagnostic_Warning => CppLogMessageType.Warning,
                            _ => CppLogMessageType.Info
                        };
                        messages.Add(new(level, text, location));
                        errors |= level == CppLogMessageType.Error;
                    }
                    if (errors) throw new InvalidOperationException("The native dependency declaration prelude is not representable by the pinned frontend: "
                        + string.Join("; ", messages.Where(message => message.type == CppLogMessageType.Error).Select(message => message.text)));
                    cancellationToken.ThrowIfCancellationRequested();
                    return unit;
                }
                catch { unit.Dispose(); throw; }
            }
        }
        finally { if (result is null) Cleanup(root); }
    }

    public void Dispose() => Cleanup(m_root);

    private static void Cleanup(string root)
    {
        if (!Directory.Exists(root)) return;
        if (PhysicalPathResolver.Resolve(root) != root) throw new IOException("The private declaration prelude directory was retargeted.");
        foreach (string file in Directory.EnumerateFileSystemEntries(root))
        {
            FileAttributes attributes = File.GetAttributes(file);
            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                throw new IOException("The private declaration prelude contains an unexpected linked or directory entry.");
            File.Delete(file);
        }
        Directory.Delete(root);
    }

    private static bool Within(
        string path,
        string root
    )
        => path.Equals(root, Comparison) || path.StartsWith(root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, Comparison);
    private static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    // Exact LLVM llvmorg-20.1.2 PPMacroExpansion.cpp RegisterBuiltinMacros inventory;
    // these dynamic macros are absent from DetailedPreprocessingRecord definitions.
    private static readonly string[] DynamicBuiltins =
    [
        "_Pragma", "__BASE_FILE__", "__COUNTER__", "__DATE__", "__FILE_NAME__", "__FILE__", "__FLT_EVAL_METHOD__", "__INCLUDE_LEVEL__",
        "__LINE__", "__MODULE__", "__TIMESTAMP__", "__TIME__", "__building_module", "__has_attribute", "__has_builtin", "__has_c_attribute",
        "__has_constexpr_builtin", "__has_cpp_attribute", "__has_declspec_attribute", "__has_embed", "__has_extension", "__has_feature",
        "__has_include", "__has_include_next", "__has_warning", "__identifier", "__is_identifier", "__is_target_arch", "__is_target_environment",
        "__is_target_os", "__is_target_variant_environment", "__is_target_variant_os", "__is_target_vendor", "__pragma"
    ];
}
