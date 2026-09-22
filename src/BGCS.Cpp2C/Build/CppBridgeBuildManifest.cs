namespace BGCS.Cpp2C.Build;

/// <summary>
/// Machine-readable, target-specific input contract for compiling a generated C++ bridge.
/// All file-system paths are relative to the manifest directory when they can be represented on the same root.
/// </summary>
public sealed record CppBridgeBuildManifest(
    int ManifestVersion,
    string TargetIdentifier,
    string? TargetTriple,
    string? TargetSysRoot,
    string? CompilerPath,
    string LanguageStandard,
    string LibraryName,
    IReadOnlyList<string> SourceFiles,
    IReadOnlyList<string> PublicHeaderFiles,
    IReadOnlyList<string> OriginalHeaderFiles,
    IReadOnlyList<string> IncludeDirectories,
    IReadOnlyList<string> SystemIncludeDirectories,
    IReadOnlyList<string> Defines,
    IReadOnlyList<string> CompilerArguments,
    IReadOnlyList<string> LibrarySearchDirectories,
    IReadOnlyList<string> LinkLibraries,
    IReadOnlyList<string> LinkerArguments);
