using System.Collections.Immutable;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Targets;

namespace BGCS.Contracts.Requests;

/// <summary>Declares one native compilation unit and its exact source-language standard.</summary>
/// <param name="path">The absolute source input path.</param>
/// <param name="language">The explicitly selected source language.</param>
/// <param name="standard">The source-language standard.</param>
public sealed record NativeCompilationUnit(
    string path,
    SourceLanguage language,
    string standard
);

/// <summary>Identifies the native artifact's linking and consumption form.</summary>
public enum NativeOutputKind
{
    /// <summary>A dynamically loaded native library.</summary>
    SharedLibrary,
    /// <summary>An archive linked into the consuming application.</summary>
    StaticLibrary,
    /// <summary>A relocatable object linked into the consuming application.</summary>
    Object
}

/// <summary>Freezes native construction inputs before a build provider runs.</summary>
/// <param name="target">The concrete target and SDK.</param>
/// <param name="sources">The complete ordered compilation inputs.</param>
/// <param name="workingDirectory">The caller-owned scratch directory, separate from formal output.</param>
/// <param name="outputFile">The declared scratch artifact path.</param>
public sealed record NativeBuildRequest(
    NativeTargetDescriptor target,
    ImmutableArray<NativeCompilationUnit> sources,
    string workingDirectory,
    string outputFile
)
{
    /// <summary>Gets source inputs to preprocess under matching compilation flags without compiling or linking an additional translation unit.</summary>
    public ImmutableArray<NativeCompilationUnit> preprocessingSources { get; init; } = [];
    /// <summary>Gets the captured directory for relative driver inputs; null uses the owned scratch directory for direct callers.</summary>
    public string? argumentWorkingDirectory { get; init; }
    /// <summary>Gets owned candidate and output roots excluded only from driver search membership before planning; actual read files stay hashed.</summary>
    public ImmutableArray<string> snapshotExcludedDirectories { get; init; } = [];
    /// <summary>Gets the requested native output form.</summary>
    public NativeOutputKind outputKind { get; init; } = NativeOutputKind.SharedLibrary;
    /// <summary>Gets ordered normal include directories.</summary>
    public ImmutableArray<string> includeDirectories { get; init; } = [];
    /// <summary>Gets ordered system header directories.</summary>
    public ImmutableArray<string> systemIncludeDirectories { get; init; } = [];
    /// <summary>Gets compiler macro definitions.</summary>
    public ImmutableArray<string> defines { get; init; } = [];
    /// <summary>Gets additional individual compiler arguments.</summary>
    public ImmutableArray<string> compilerArguments { get; init; } = [];
    /// <summary>Gets additional individual linker arguments.</summary>
    public ImmutableArray<string> linkerArguments { get; init; } = [];
    /// <summary>Gets native dependency library names or absolute paths.</summary>
    public ImmutableArray<string> libraries { get; init; } = [];
    /// <summary>Gets absolute library search paths.</summary>
    public ImmutableArray<string> libraryDirectories { get; init; } = [];
}

/// <summary>Describes one shell-free compiler or linker operation.</summary>
/// <param name="id">The stable operation name.</param>
/// <param name="executable">The resolved tool executable.</param>
/// <param name="arguments">Individual immutable arguments.</param>
/// <param name="workingDirectory">The explicit operation directory.</param>
public sealed record NativeBuildCommand(
    string id,
    string executable,
    ImmutableArray<string> arguments,
    string workingDirectory
)
{
    /// <summary>Gets the linker trace format when this command proves the final linker input closure.</summary>
    public NativeLinkTraceFormat? linkDependencyTrace { get; init; }
    /// <summary>Gets explicit child environment changes; null removes an inherited variable.</summary>
    public ImmutableDictionary<string, string?> environmentOverrides { get; init; } = ImmutableDictionary<string, string?>.Empty;
    /// <summary>Gets explicit inputs on which this operation depends.</summary>
    public ImmutableArray<string> inputFiles { get; init; } = [];
    /// <summary>Gets files that the operation must produce.</summary>
    public ImmutableArray<string> outputFiles { get; init; } = [];
}

/// <summary>Describes a provider-authored build file whose publication belongs to the unified executor.</summary>
/// <param name="relativePath">The portable relative path within the owned scratch directory.</param>
/// <param name="content">The complete immutable UTF-8 build-file contents.</param>
public sealed record NativeBuildInput(
    string relativePath,
    string content
);

/// <summary>Identifies the dependency format written by the selected compiler.</summary>
public enum NativeDependencyFormat
{
    /// <summary>A Make-style compiler dependency file.</summary>
    Make,
    /// <summary>The MSVC sourceDependencies JSON format.</summary>
    MsvcJson
}

/// <summary>Identifies a complete compiler-authored dependency list.</summary>
/// <param name="path">The absolute compiler dependency file.</param>
/// <param name="format">The compiler-authored dependency format.</param>
/// <param name="source">The absolute source unit whose dependency closure is recorded.</param>
public sealed record NativeDependencyFile(
    string path,
    NativeDependencyFormat format,
    string source
)
{
    /// <summary>Gets the actual compiler invocation directory used to resolve emitted relative dependency paths.</summary>
    public string? workingDirectory { get; init; }
}

/// <summary>Plans an independent dependency scan before native compilation begins.</summary>
/// <param name="command">The exact compiler scan invocation.</param>
/// <param name="dependencyFile">The dependency list produced by this scan.</param>
public sealed record NativeDependencyScan(
    NativeBuildCommand command,
    NativeDependencyFile dependencyFile
);

/// <summary>Preserves an exact compiler preprocessing operation for independent final-source fact verification.</summary>
/// <param name="command">The native compiler operation, using the same frozen source options as compilation.</param>
/// <param name="source">The source unit whose effective declarations are checked.</param>
/// <param name="language">The source language.</param>
/// <param name="languageStandard">The exact compilation standard.</param>
/// <param name="outputFile">The owned preprocessed source file.</param>
public sealed record NativePreprocessingOperation(
    NativeBuildCommand command,
    string source,
    SourceLanguage language,
    string languageStandard,
    string outputFile
)
{
    /// <summary>Gets the actual preprocessing dependency output, independently compared with its preflight scan.</summary>
    public NativeDependencyFile? dependencyFile { get; init; }
    /// <summary>Gets whether this input must be preprocessed without creating a compiled or linked translation unit.</summary>
    public bool preprocessingOnly { get; init; }
}

/// <summary>Identifies the linker's native resolved-input trace format.</summary>
public enum NativeLinkTraceFormat
{
    /// <summary>GNU, LLVM or Apple linker trace output.</summary>
    GnuLike,
    /// <summary>MSVC or compatible verbose library-search output.</summary>
    MsvcLibrarySearch
}

/// <summary>Plans an independent link probe after object construction and before final native linking.</summary>
/// <param name="finalCommandId">The final link operation whose resolved inputs are checked.</param>
/// <param name="command">The same linker inputs and options with private probe output names.</param>
/// <param name="format">The native linker trace format.</param>
public sealed record NativeLinkDependencyScan(
    string finalCommandId,
    NativeBuildCommand command,
    NativeLinkTraceFormat format
);

/// <summary>Owns an immutable ordered build plan whose execution belongs to the host.</summary>
/// <param name="providerId">The native build provider identity.</param>
/// <param name="target">The concrete target ABI.</param>
/// <param name="commands">The ordered compiler and linker operations.</param>
/// <param name="outputFile">The exact expected native artifact.</param>
public sealed record NativeBuildPlan(
    string providerId,
    NativeTargetDescriptor target,
    ImmutableArray<NativeBuildCommand> commands,
    string outputFile
)
{
    /// <summary>Gets response-file bytes read while freezing compiler and linker arguments, including recursively referenced files.</summary>
    public SourceSnapshot argumentInputs { get; init; } = new([], []);
    /// <summary>Gets host-owned artifact roots omitted only from search membership; actual compiler-read bytes remain tracked.</summary>
    public ImmutableArray<string> snapshotExcludedDirectories { get; init; } = [];
    /// <summary>Gets the artifact's native consumption form.</summary>
    public NativeOutputKind outputKind { get; init; } = NativeOutputKind.SharedLibrary;
    /// <summary>Gets the owned absolute scratch directory containing provider-authored files.</summary>
    public string? scratchDirectory { get; init; }
    /// <summary>Gets provider-authored inputs to write only inside the owned scratch directory.</summary>
    public ImmutableArray<NativeBuildInput> generatedInputFiles { get; init; } = [];
    /// <summary>Gets the underlying native compiler family for independent target identity checks.</summary>
    public string? compilerFamily { get; init; }
    /// <summary>Gets the exact underlying native driver path.</summary>
    public string? compilerPath { get; init; }
    /// <summary>Gets compiler dependency scans to execute before content snapshots are frozen.</summary>
    public ImmutableArray<NativeDependencyScan> dependencyScans { get; init; } = [];
    /// <summary>Gets exact underlying compiler preprocessing operations; wrapper build systems preserve these native commands.</summary>
    public ImmutableArray<NativePreprocessingOperation> preprocessingOperations { get; init; } = [];
    /// <summary>Gets dependency files produced by the actual compiler operations.</summary>
    public ImmutableArray<NativeDependencyFile> dependencyFiles { get; init; } = [];
    /// <summary>Gets ordered native header search roots whose membership must stay unchanged during construction.</summary>
    public ImmutableArray<string> includeSearchDirectories { get; init; } = [];
    /// <summary>Gets exact compiler, linker, build-tool and explicitly named library files affecting construction.</summary>
    public ImmutableArray<string> externalInputFiles { get; init; } = [];
    /// <summary>Gets ordered library search roots whose membership can change linker resolution.</summary>
    public ImmutableArray<string> librarySearchDirectories { get; init; } = [];
    /// <summary>Gets probes used to freeze implicit library and linker-script inputs before final linking.</summary>
    public ImmutableArray<NativeLinkDependencyScan> linkDependencyScans { get; init; } = [];
}
