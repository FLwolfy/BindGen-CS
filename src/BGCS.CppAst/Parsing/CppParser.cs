// Portions of this file are modified from original work by Alexandre Mutel.
// Modified by BGCS contributors.
// Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BGCS.CppAst.Interop;
using BGCS.CppAst.Model.Metadata;
using ClangSharp.Interop;

namespace BGCS.CppAst.Parsing;

/// <summary>
/// Parses configured native source into owned compilations that retain Clang lifetimes until disposal.
/// </summary>
public static class CppParser
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
        var arguments = new List<string>();
        // Make sure that paths are absolute
        var normalizedIncludePaths = new List<string>();
        normalizedIncludePaths.AddRange(options.includeFolders.Select(x => Path.Combine(Environment.CurrentDirectory, x)));
        var normalizedSystemIncludePaths = new List<string>();
        normalizedSystemIncludePaths.AddRange(options.systemIncludeFolders.Select(x => Path.Combine(Environment.CurrentDirectory, x)));
        arguments.AddRange(options.additionalArguments);
        arguments.Add("-resource-dir=" + ClangResourceHeaders.directory);
        arguments.AddRange(normalizedIncludePaths.Select(x => $"-I{x}"));
        arguments.AddRange(normalizedSystemIncludePaths.Select(x => $"-isystem{x}"));
        arguments.AddRange(options.defines.Select(x => $"-D{x}"));
        arguments.Add("-dM");
        arguments.Add("-E");
        switch (options.parserKind)
        {
            case CppParserKind.None:
                break;
            case CppParserKind.Cpp:
                arguments.Add("-xc++");
                break;
            case CppParserKind.C:
                arguments.Add("-xc");
                break;
            case CppParserKind.ObjC:
                arguments.Add("-xobjective-c");
                // Blocks are part of the Objective-C surface modeled by CppBlockFunctionType.
                // Clang does not enable the extension uniformly for every non-Apple host,
                // even when an Apple target triple is selected.
                arguments.Add("-fblocks");
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        if (!arguments.Any(x => x.StartsWith("--target=")))
        {
            arguments.Add($"--target={GetTripleFromOptions(options)}");
        }

        if (options.parseComments)
        {
            arguments.Add("-fparse-all-comments");
        }

        var translationFlags = CXTranslationUnit_Flags.CXTranslationUnit_None;
        translationFlags |= CXTranslationUnit_Flags.CXTranslationUnit_SkipFunctionBodies; // Don't traverse function bodies
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
            string rootFileName = global::BGCS.CppAst.Parsing.CppParser.C_CPPASTROOTFILENAME;
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
                        var message = GetMessageAndLocation(rootFileContent, diagnostic, out var location);
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
            }

            return compilation;
        }
    }

    private static string GetMessageAndLocation(
        string rootContent,
        CXDiagnostic diagnostic,
        out CppSourceLocation location
    ) {
        var builder = new StringBuilder();
        builder.Append(diagnostic.ToString());
        location = diagnostic.GetSourceLocation();
        if (location.file == global::BGCS.CppAst.Parsing.CppParser.C_CPPASTROOTFILENAME)
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

        diagnostic.Dispose();
        return builder.ToString();
    }

    private static string GetTripleFromOptions(CppParserOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.targetTriple);
        return options.targetTriple;
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
