using BGCS.Contracts.Inputs;
// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BGCS.Frontend.Clang.Interop;
using BGCS.Frontend.Clang.Model.Metadata;
using BGCS.Frontend.Clang.Model;
using BGCS.Frontend.Clang.Model.Declarations;
using BGCS.Frontend.Clang.Model.Interfaces;
using BGCS.Frontend.Clang.Model.Templates;
using BGCS.Frontend.Clang.Model.Types;
using BGCS.Frontend.Clang.Utilities;
using ClangSharp.Interop;

namespace BGCS.Frontend.Clang.Parsing;

/// <summary>
/// Parses configured native source into owned compilations that retain Clang lifetimes until disposal.
/// </summary>
internal static class CppParser
{
    /// <summary>
    /// Identifies the synthetic umbrella file that includes all inputs and parser preamble declarations.
    /// </summary>
    public const string C_CPPASTROOTFILENAME = "cppast.input";
    /// <summary>
    /// Parse the specified C++ text in-memory.
    /// </summary>
    /// <param name = "cppText">A string with a C/C++ text</param>
    /// <param name = "options">Options used for parsing this file (e.g include folders...)</param>
    /// <param name = "cppFilename">Optional path to a file only used for reporting errors. Default is 'content'</param>
    /// <returns>The result of the compilation</returns>
    public static CppCompilation Parse(
        string cppText,
        CppParserOptions? options = null,
        string cppFilename = "content"
    ) {
        if (cppText == null)
            throw new ArgumentNullException(nameof(cppText));
        var cppFiles = new List<CppFileOrString>
        {
            new CppFileOrString()
            {
                filename = cppFilename,
                content = cppText,
            }
        };
        return ParseInternal(cppFiles, options);
    }

    /// <summary>
    /// Parse the specified single file.
    /// </summary>
    /// <param name = "cppFilename">A path to a C/C++ file on the disk to parse</param>
    /// <param name = "options">Options used for parsing this file (e.g include folders...)</param>
    /// <returns>The result of the compilation</returns>
    public static CppCompilation ParseFile(
        string cppFilename,
        CppParserOptions? options = null
    ) {
        if (cppFilename == null)
            throw new ArgumentNullException(nameof(cppFilename));
        var files = new List<string>()
        {
            cppFilename
        };
        return ParseFiles(files, options);
    }

    /// <summary>
    /// Parse the specified single file.
    /// </summary>
    /// <param name = "cppFilenameList">A list of path to C/C++ header files on the disk to parse</param>
    /// <param name = "options">Options used for parsing this file (e.g include folders...)</param>
    /// <returns>The result of the compilation</returns>
    public static CppCompilation ParseFiles(
        List<string> cppFilenameList,
        CppParserOptions? options = null
    ) {
        if (cppFilenameList == null)
            throw new ArgumentNullException(nameof(cppFilenameList));
        var cppFiles = new List<CppFileOrString>();
        foreach (var cppFilepath in cppFilenameList)
        {
            if (string.IsNullOrEmpty(cppFilepath))
                throw new InvalidOperationException("A null or empty filename is invalid in the list");
            cppFiles.Add(new CppFileOrString() { filename = cppFilepath });
        }

        return ParseInternal(cppFiles, options);
    }

    /// <summary>
    /// Private method parsing file or content.
    /// </summary>
    /// <param name = "cppFiles">A list of path to C/C++ header files on the disk to parse</param>
    /// <param name = "options">Options used for parsing this file (e.g include folders...)</param>
    /// <returns>The result of the compilation</returns>
    private static unsafe CppCompilation ParseInternal(
        List<CppFileOrString> cppFiles,
        CppParserOptions? options = null
    ) {
        if (cppFiles == null)
            throw new ArgumentNullException(nameof(cppFiles));
        ClangNativeRuntime.EnsureLoaded();
        options = options ?? new CppParserOptions();
        var arguments = CreateArguments(options);

        var translationFlags = CXTranslationUnit_Flags.CXTranslationUnit_None;
        // C header helpers need authoritative definition facts before we can
        // generate real exports. Skipping a body makes libclang report its
        // declaration as non-defining even when the source supplies that body.
        if (options.parserKind != SourceLanguage.C && !options.retainFunctionBodies)
            translationFlags |= CXTranslationUnit_Flags.CXTranslationUnit_SkipFunctionBodies;
        translationFlags |= CXTranslationUnit_Flags.CXTranslationUnit_IncludeAttributedTypes; // Include attributed types in CXType
        translationFlags |= CXTranslationUnit_Flags.CXTranslationUnit_VisitImplicitAttributes; // Implicit attributes should be visited
        if (options.parseMacros)
        {
            translationFlags |= CXTranslationUnit_Flags.CXTranslationUnit_DetailedPreprocessingRecord;
        }

        translationFlags |= CXTranslationUnit_Flags.CXTranslationUnit_DetailedPreprocessingRecord;
        var argumentsArray = arguments.ToArray();
        using (var createIndex = CXIndex.Create())
        {
            string rootFileName = global::BGCS.Frontend.Clang.Parsing.CppParser.C_CPPASTROOTFILENAME;
            string? rootFileContent = null;
            // Build the root input source file
            var tempBuilder = new StringBuilder();
            if (options.preHeaderText != null)
            {
                tempBuilder.AppendLine(options.preHeaderText);
            }

            foreach (var file in cppFiles)
            {
                if (file.content != null)
                {
                    tempBuilder.AppendLine(file.content);
                }
                else
                {
                    var filePath = Path.Combine(Environment.CurrentDirectory, file.filename);
                    tempBuilder.AppendLine($"#include \"{filePath}\"");
                }
            }

            if (options.postHeaderText != null)
            {
                tempBuilder.AppendLine(options.postHeaderText);
            }

            // TODO: Add debug
            rootFileContent = tempBuilder.ToString();
            CXTranslationUnit translationUnit;
            using (CXUnsavedFile unsavedFile = CXUnsavedFile.Create(rootFileName, rootFileContent))
            {
                ReadOnlySpan<CXUnsavedFile> unsavedFiles = stackalloc CXUnsavedFile[]
                {
                    unsavedFile
                };
                translationUnit = CXTranslationUnit.Parse(createIndex, rootFileName, argumentsArray, unsavedFiles, translationFlags);
            }

            CppModelBuilder builder = new(translationUnit)
            {
                sourcePaths = new SourcePathMapper(options.sourcePathMappings),
                autoSquashTypedef = options.autoSquashTypedef,
                parserKind = options.parserKind,
                parseSystemIncludes = options.parseSystemIncludes,
                parseCommentsEnabled = options.parseComments,
                parseTokenAttributeEnabled = options.parseTokenAttributes,
                parseCommentAttributeEnabled = options.parseCommentAttribute,
            };
            var compilation = builder.rootCompilation;
            compilation.inputText = rootFileContent;
            bool skipProcessing = false;
            if (translationUnit.NumDiagnostics != 0)
            {
                for (uint i = 0; i < translationUnit.NumDiagnostics; ++i)
                {
                    using (var diagnostic = translationUnit.GetDiagnostic(i))
                    {
                        var message = GetMessageAndLocation(rootFileContent, diagnostic, out var location, options.usePresumedSourceLocations);
                        switch (diagnostic.Severity)
                        {
                            case CXDiagnosticSeverity.CXDiagnostic_Ignored:
                            case CXDiagnosticSeverity.CXDiagnostic_Note:
                                compilation.diagnostics.Info(message, location);
                                break;
                            case CXDiagnosticSeverity.CXDiagnostic_Warning:
                                // Avoid warning from clang (0, 0): warning: argument unused during compilation: '-fsyntax-only'
                                if (!message.Contains("-fsyntax-only"))
                                {
                                    compilation.diagnostics.Warning(message, location);
                                }

                                break;
                            case CXDiagnosticSeverity.CXDiagnostic_Error:
                            case CXDiagnosticSeverity.CXDiagnostic_Fatal:
                                compilation.diagnostics.Error(message, location);
                                skipProcessing = true;
                                break;
                        }
                    }
                }
            }

            if (skipProcessing)
            {
                compilation.diagnostics.Warning($"Compilation aborted due to one or more errors listed above.", new CppSourceLocation(rootFileName, 0, 1, 1));
            }
            else
            {
                translationUnit.Cursor.VisitChildren(builder.VisitTranslationUnit, clientData: default);
                NativeCallableAddressabilityFactReader.Read(createIndex, argumentsArray, rootFileContent, compilation);
                var literalExtents = NativeStringConstantFactReader.ReadLiteralExtents(createIndex, argumentsArray, compilation);
                NativeConstantFactReader.ReadFields(compilation, literalExtents);
                if (options.parseMacros)
                    NativeConstantFactReader.ReadMacros(createIndex, argumentsArray, rootFileContent, compilation, options.parserKind, options.parseSystemIncludes, literalExtents);
                if (options.parserKind == SourceLanguage.Cpp)
                    NativeCallableExceptionFactReader.Read(createIndex, argumentsArray, rootFileContent, compilation);
                if (options.parserKind == SourceLanguage.Cpp)
                    NativeReferenceStorageFactReader.Read(createIndex, argumentsArray, compilation);
                if (options.parserKind == SourceLanguage.Cpp)
                    EvaluateDefaultConstructibility(createIndex, argumentsArray, rootFileContent, compilation);
                if (options.parserKind == SourceLanguage.Cpp && compilation.primitiveFacts.Keys.Any(type => type.kind == CppPrimitiveKind.WChar))
                {
                    bool? signed = EvaluateWcharSignedness(createIndex, argumentsArray, compilation);
                    foreach (CppPrimitiveType primitive in compilation.primitiveFacts.Keys.Where(type => type.kind == CppPrimitiveKind.WChar).ToArray())
                        compilation.primitiveFacts[primitive] = compilation.primitiveFacts[primitive] with { IsSigned = signed };
                    if (signed is null)
                        compilation.diagnostics.Warning("The compiler could not independently determine builtin wchar_t signedness using the configured target arguments.");
                }
            }

            return compilation;
        }
    }

    private static unsafe bool? EvaluateWcharSignedness(
        CXIndex index,
        string[] arguments,
        CppCompilation compilation
    ) {
        // A separate, tiny translation unit keeps compiler probes out of the user
        // declaration graph and preserves the exact target and language arguments.
        // It is needed only when the main parse actually encountered builtin wchar_t.
        const string file = "bgcs.compiler.primitive-facts.cpp";
        const string source = "enum bgcs_wchar_probe { bgcs_wchar_signed = (wchar_t(-1) < wchar_t(0)) ? 1 : 0 };";
        using CXUnsavedFile input = CXUnsavedFile.Create(file, source);
        ReadOnlySpan<CXUnsavedFile> inputs = stackalloc CXUnsavedFile[] { input };
        using CXTranslationUnit probe = CXTranslationUnit.Parse(index, file, arguments, inputs, CXTranslationUnit_Flags.CXTranslationUnit_None);
        for (uint indexOfDiagnostic = 0; indexOfDiagnostic < probe.NumDiagnostics; indexOfDiagnostic++)
        {
            using CXDiagnostic diagnostic = probe.GetDiagnostic(indexOfDiagnostic);
            if (diagnostic.Severity is CXDiagnosticSeverity.CXDiagnostic_Error or CXDiagnosticSeverity.CXDiagnostic_Fatal) return null;
        }
        bool? signed = null;
        probe.Cursor.VisitChildren((
            cursor,
            _,
            _
        ) => {
            if (cursor.Kind != CXCursorKind.CXCursor_EnumDecl || CXUtil.GetCursorSpelling(cursor) != "bgcs_wchar_probe")
                return CXChildVisitResult.CXChildVisit_Continue;
            cursor.VisitChildren((
                value,
                _,
                _
            ) => {
                if (value.Kind == CXCursorKind.CXCursor_EnumConstantDecl && CXUtil.GetCursorSpelling(value) == "bgcs_wchar_signed")
                {
                    long result = value.EnumConstantDeclValue;
                    if (result is 0 or 1) signed = result == 1;
                }
                return CXChildVisitResult.CXChildVisit_Continue;
            }, default);
            return CXChildVisitResult.CXChildVisit_Continue;
        }, default);
        return signed;
    }

    private static unsafe void EvaluateDefaultConstructibility(
        CXIndex index,
        string[] arguments,
        string headers,
        CppCompilation compilation
    ) {
        CppClass[] candidates = Records(compilation).Where(record => record.classKind is CppClassKind.Class or CppClassKind.Struct or CppClassKind.Union
            && record.isCompleteDefinition && !record.isAnonymous
            && !record.cursor.Location.IsInSystemHeader && Accessible(record)
            && record.templateKind is CppTemplateKind.NormalClass or CppTemplateKind.TemplateSpecializedClass
            && !record.fullName.Contains('?', StringComparison.Ordinal) && !record.fullName.Contains('(', StringComparison.Ordinal)).Distinct().ToArray();
        if (candidates.Length == 0) return;
        string stem = "bgcs_record_traits_" + Guid.NewGuid().ToString("N");
        StringBuilder source = new(headers);
        source.Append("\nenum ").Append(stem).AppendLine(" {");
        for (int candidate = 0; candidate < candidates.Length; candidate++)
        {
            string name = "::" + candidates[candidate].fullName.TrimStart(':');
            source.Append(stem).Append('_').Append(candidate * 3).Append(" = __is_constructible(").Append(name).AppendLine(") ? 1 : 0,");
            source.Append(stem).Append('_').Append(candidate * 3 + 1).Append(" = __is_standard_layout(").Append(name).AppendLine(") ? 1 : 0,");
            source.Append(stem).Append('_').Append(candidate * 3 + 2).Append(" = __is_trivially_copyable(").Append(name).AppendLine(") ? 1 : 0,");
        }
        source.AppendLine("};");
        // Preserve the umbrella identity as well as arguments: __BASE_FILE__ and
        // preamble __FILE__ expressions must have the same value as the main parse.
        using CXUnsavedFile input = CXUnsavedFile.Create(C_CPPASTROOTFILENAME, source.ToString());
        ReadOnlySpan<CXUnsavedFile> inputs = stackalloc CXUnsavedFile[] { input };
        using CXTranslationUnit probe = CXTranslationUnit.Parse(index, C_CPPASTROOTFILENAME, arguments, inputs,
            CXTranslationUnit_Flags.CXTranslationUnit_SkipFunctionBodies);
        for (uint diagnosticIndex = 0; diagnosticIndex < probe.NumDiagnostics; diagnosticIndex++)
        {
            using CXDiagnostic diagnostic = probe.GetDiagnostic(diagnosticIndex);
            if (diagnostic.Severity is CXDiagnosticSeverity.CXDiagnostic_Error or CXDiagnosticSeverity.CXDiagnostic_Fatal)
            {
                compilation.diagnostics.Warning("The compiler could not evaluate native record traits; implicit constructors and direct DTO storage remain unavailable until independently established.");
                return;
            }
        }
        probe.Cursor.VisitChildren((
            cursor,
            _,
            _
        ) => {
            if (cursor.Kind != CXCursorKind.CXCursor_EnumDecl || CXUtil.GetCursorSpelling(cursor) != stem) return CXChildVisitResult.CXChildVisit_Continue;
            cursor.VisitChildren((
                value,
                _,
                _
            ) => {
                string name = CXUtil.GetCursorSpelling(value);
                if (value.Kind == CXCursorKind.CXCursor_EnumConstantDecl && name.StartsWith(stem + "_", StringComparison.Ordinal)
                    && int.TryParse(name.AsSpan(stem.Length + 1), out int encoded) && encoded >= 0 && encoded / 3 < candidates.Length)
                {
                    CppClass record = candidates[encoded / 3];
                    bool? result = value.EnumConstantDeclValue == 1 ? true : value.EnumConstantDeclValue == 0 ? false : null;
                    if (encoded % 3 == 0) compilation.defaultConstructionFacts[record] = result;
                    else
                    {
                        compilation.recordStorageTraits.TryGetValue(record, out var traits);
                        compilation.recordStorageTraits[record] = encoded % 3 == 1 ? (result, traits.TriviallyCopyable) : (traits.StandardLayout, result);
                    }
                }
                return CXChildVisitResult.CXChildVisit_Continue;
            }, default);
            return CXChildVisitResult.CXChildVisit_Continue;
        }, default);

        static IEnumerable<CppClass> Records(ICppContainer container)
        {
            foreach (ICppDeclaration declaration in container.children)
            {
                if (declaration is CppClass record) yield return record;
                if (declaration is ICppContainer child)
                    foreach (CppClass nested in Records(child)) yield return nested;
            }
        }
        static bool Accessible(CppElement element)
        {
            if (element is CppClass record && (record.visibility is CppVisibility.Private or CppVisibility.Protected
                || record.templateKind is CppTemplateKind.TemplateClass or CppTemplateKind.PartialTemplateClass)) return false;
            return element.parent is not CppElement parent || Accessible(parent);
        }
    }

    internal static string GetMessageAndLocation(
        string rootContent,
        CXDiagnostic diagnostic,
        out CppSourceLocation location,
        bool usePresumedSourceLocations
    ) {
        var builder = new StringBuilder();
        builder.Append(diagnostic.ToString());
        location = usePresumedSourceLocations ? diagnostic.Location.ToPresumedSourceLocation() : diagnostic.GetSourceLocation();
        if (location.file == global::BGCS.Frontend.Clang.Parsing.CppParser.C_CPPASTROOTFILENAME)
        {
            var reader = new StringReader(rootContent);
            var lines = new List<string>();
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                lines.Add(line);
            }

            var lineIndex = location.line - 1;
            if (lineIndex < lines.Count)
            {
                builder.AppendLine();
                builder.AppendLine(lines[lineIndex]);
                for (int i = 0; i < location.column - 1; i++)
                {
                    builder.Append(i + 1 == location.column - 1 ? "-" : " ");
                }

                builder.AppendLine("^-");
            }
        }

        return builder.ToString();
    }

    internal static List<string> CreateArguments(CppParserOptions options)
    {
        var arguments = new List<string>();
        // Make sure that paths are absolute
        var normalizedIncludePaths = new List<string>();
        normalizedIncludePaths.AddRange(options.includeFolders.Select(x => Path.Combine(Environment.CurrentDirectory, x)));
        arguments.AddRange(options.additionalArguments);
        // Explicit configs are expanded and locked by the host; ambient driver
        // configs must not change either the primary parse or its fact probes.
        arguments.Add("--no-default-config");
        arguments.Add("-resource-dir=" + ClangResourceHeaders.directory);
        arguments.AddRange(normalizedIncludePaths.Select(x => $"-I{x}"));
        options.AppendSystemIncludeArguments(arguments);
        arguments.AddRange(options.defines.Select(x => $"-D{x}"));
        arguments.Add("-dM");
        arguments.Add("-E");
        switch (options.parserKind)
        {
            case SourceLanguage.None:
                break;
            case SourceLanguage.Cpp:
                arguments.Add("-xc++");
                break;
            case SourceLanguage.C:
                arguments.Add("-xc");
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        if (!arguments.Any(x => x.StartsWith("--target=")) && !string.IsNullOrWhiteSpace(options.targetTriple))
        {
            arguments.Add($"--target={options.targetTriple}");
        }

        if (options.parseComments)
        {
            arguments.Add("-fparse-all-comments");
        }

        return arguments;
    }

    private struct CppFileOrString
    {
        /// <summary>
        /// Exposes public member <c>Filename</c>.
        /// </summary>
        public string filename;
        /// <summary>
        /// Exposes public member <c>Content</c>.
        /// </summary>
        public string content;
        /// <summary>
        /// Executes public operation <c>ToString</c>.
        /// </summary>
        public override string ToString()
        {
            return $"{nameof(this.filename)}: {this.filename}, {nameof(this.content)}: {this.content}";
        }
    }
}
