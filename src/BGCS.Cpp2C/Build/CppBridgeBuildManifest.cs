using System;
using System.Collections.Generic;
using System.Linq;

namespace BGCS.Cpp2C.Build;

/// <summary>
/// Machine-readable, target-specific input contract for compiling a generated C++ bridge.
/// All file-system paths are relative to the manifest directory when they can be represented on the same root.
/// </summary>
/// <param name="targetIdentifier">
/// Stable native target identifier used to select the ABI and output layout.
/// </param>
/// <param name="targetTriple">
/// Optional Clang target triple; null uses the target provider selection.
/// </param>
/// <param name="targetSysRoot">
/// Optional SDK sysroot, resolved from the manifest directory.
/// </param>
/// <param name="compilerPath">
/// Optional compiler executable path requested by the target toolchain.
/// </param>
/// <param name="languageStandard">
/// C++ language standard required by the generated bridge.
/// </param>
/// <param name="libraryName">
/// Logical native library name without a platform prefix or file extension.
/// </param>
/// <param name="sourceFiles">
/// Generated and contributed source files to compile, relative to the manifest directory.
/// </param>
/// <param name="publicHeaderFiles">
/// Generated public C ABI headers used for binding and export validation.
/// </param>
/// <param name="originalHeaderFiles">
/// Facade input headers belonging to the parsed source closure.
/// </param>
/// <param name="includeDirectories">
/// Ordered normal include search directories for the compiler.
/// </param>
/// <param name="systemIncludeDirectories">
/// Ordered SDK include search directories classified as system headers.
/// </param>
/// <param name="defines">
/// Compiler preprocessor definitions represented without the -D prefix.
/// </param>
/// <param name="compilerArguments">
/// Individual compiler arguments appended without shell interpretation.
/// </param>
/// <param name="librarySearchDirectories">
/// Linker library search directories relative to the manifest directory.
/// </param>
/// <param name="linkLibraries">
/// Required native libraries passed to the selected linker provider.
/// </param>
/// <param name="linkerArguments">
/// Individual linker arguments appended without shell interpretation.
/// </param>
public sealed record CppBridgeBuildManifest(
    string targetIdentifier,
    string? targetTriple,
    string? targetSysRoot,
    string? compilerPath,
    string languageStandard,
    string libraryName,
    IReadOnlyList<string> sourceFiles,
    IReadOnlyList<string> publicHeaderFiles,
    IReadOnlyList<string> originalHeaderFiles,
    IReadOnlyList<string> includeDirectories,
    IReadOnlyList<string> systemIncludeDirectories,
    IReadOnlyList<string> defines,
    IReadOnlyList<string> compilerArguments,
    IReadOnlyList<string> librarySearchDirectories,
    IReadOnlyList<string> linkLibraries,
    IReadOnlyList<string> linkerArguments
)
{
    private IReadOnlyList<string> m_sourceFiles = Snapshot(sourceFiles, nameof(sourceFiles));
    private IReadOnlyList<string> m_publicHeaderFiles = Snapshot(publicHeaderFiles, nameof(publicHeaderFiles));
    private IReadOnlyList<string> m_originalHeaderFiles = Snapshot(originalHeaderFiles, nameof(originalHeaderFiles));
    private IReadOnlyList<string> m_includeDirectories = Snapshot(includeDirectories, nameof(includeDirectories));
    private IReadOnlyList<string> m_systemIncludeDirectories = Snapshot(systemIncludeDirectories, nameof(systemIncludeDirectories));
    private IReadOnlyList<string> m_defines = Snapshot(defines, nameof(defines));
    private IReadOnlyList<string> m_compilerArguments = Snapshot(compilerArguments, nameof(compilerArguments));
    private IReadOnlyList<string> m_librarySearchDirectories = Snapshot(librarySearchDirectories, nameof(librarySearchDirectories));
    private IReadOnlyList<string> m_linkLibraries = Snapshot(linkLibraries, nameof(linkLibraries));
    private IReadOnlyList<string> m_linkerArguments = Snapshot(linkerArguments, nameof(linkerArguments));

    /// <summary>
    /// Generated and contributed source files to compile, relative to the manifest directory. Assignment copies the sequence into a read-only snapshot.
    /// </summary>
    public IReadOnlyList<string> sourceFiles
    {
        get => m_sourceFiles;
        init => m_sourceFiles = Snapshot(value, nameof(sourceFiles));
    }

    /// <summary>
    /// Generated public C ABI headers used for binding and export validation. Assignment copies the sequence into a read-only snapshot.
    /// </summary>
    public IReadOnlyList<string> publicHeaderFiles
    {
        get => m_publicHeaderFiles;
        init => m_publicHeaderFiles = Snapshot(value, nameof(publicHeaderFiles));
    }

    /// <summary>
    /// Facade input headers belonging to the parsed source closure. Assignment copies the sequence into a read-only snapshot.
    /// </summary>
    public IReadOnlyList<string> originalHeaderFiles
    {
        get => m_originalHeaderFiles;
        init => m_originalHeaderFiles = Snapshot(value, nameof(originalHeaderFiles));
    }

    /// <summary>
    /// Ordered normal include search directories for the compiler. Assignment copies the sequence into a read-only snapshot.
    /// </summary>
    public IReadOnlyList<string> includeDirectories
    {
        get => m_includeDirectories;
        init => m_includeDirectories = Snapshot(value, nameof(includeDirectories));
    }

    /// <summary>
    /// Ordered SDK include search directories classified as system headers. Assignment copies the sequence into a read-only snapshot.
    /// </summary>
    public IReadOnlyList<string> systemIncludeDirectories
    {
        get => m_systemIncludeDirectories;
        init => m_systemIncludeDirectories = Snapshot(value, nameof(systemIncludeDirectories));
    }

    /// <summary>
    /// Compiler preprocessor definitions represented without the -D prefix. Assignment copies the sequence into a read-only snapshot.
    /// </summary>
    public IReadOnlyList<string> defines
    {
        get => m_defines;
        init => m_defines = Snapshot(value, nameof(defines));
    }

    /// <summary>
    /// Individual compiler arguments appended without shell interpretation. Assignment copies the sequence into a read-only snapshot.
    /// </summary>
    public IReadOnlyList<string> compilerArguments
    {
        get => m_compilerArguments;
        init => m_compilerArguments = Snapshot(value, nameof(compilerArguments));
    }

    /// <summary>
    /// Linker library search directories relative to the manifest directory. Assignment copies the sequence into a read-only snapshot.
    /// </summary>
    public IReadOnlyList<string> librarySearchDirectories
    {
        get => m_librarySearchDirectories;
        init => m_librarySearchDirectories = Snapshot(value, nameof(librarySearchDirectories));
    }

    /// <summary>
    /// Required native libraries passed to the selected linker provider. Assignment copies the sequence into a read-only snapshot.
    /// </summary>
    public IReadOnlyList<string> linkLibraries
    {
        get => m_linkLibraries;
        init => m_linkLibraries = Snapshot(value, nameof(linkLibraries));
    }

    /// <summary>
    /// Individual linker arguments appended without shell interpretation. Assignment copies the sequence into a read-only snapshot.
    /// </summary>
    public IReadOnlyList<string> linkerArguments
    {
        get => m_linkerArguments;
        init => m_linkerArguments = Snapshot(value, nameof(linkerArguments));
    }

    private static IReadOnlyList<string> Snapshot(
        IReadOnlyList<string> values,
        string parameterName
    ) {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        if (values.Any(value => value == null))
            throw new ArgumentException("Manifest sequences cannot contain null entries.", parameterName);
        return Array.AsReadOnly(values.ToArray());
    }
}
