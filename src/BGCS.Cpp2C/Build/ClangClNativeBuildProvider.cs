namespace BGCS.Cpp2C.Build;

/// <summary>
/// Produces Windows DLL build pipelines for the clang-cl command-line interface.
/// </summary>
public sealed class ClangClNativeBuildProvider : INativeBuildPipelineProvider
{
    private readonly string compilerPath;

    public ClangClNativeBuildProvider(string compilerPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(compilerPath);
        this.compilerPath = compilerPath;
    }

    public string Name => "clang-cl";

    public NativeBuildPipeline CreatePipeline(CppBridgeBuildManifest manifest, string manifestPath, string? outputPath = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (!manifest.TargetIdentifier.StartsWith("windows-", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("clang-cl can only build Windows bridge targets.");
        string manifestDirectory = NativeBuildPaths.GetManifestDirectory(manifestPath);
        string artifactPath = NativeBuildPaths.GetOutputPath(manifest, manifestPath, outputPath);
        List<string> arguments = ["/nologo", "/LD", "/EHsc", "/std:" + NormalizeStandard(manifest.LanguageStandard)];
        if (!string.IsNullOrWhiteSpace(manifest.TargetTriple))
            arguments.Add("/clang:--target=" + manifest.TargetTriple);
        arguments.AddRange(manifest.Defines.Select(define => "/D" + define));
        arguments.AddRange(manifest.IncludeDirectories.Select(include => "/I" + NativeBuildPaths.Resolve(manifestDirectory, include)));
        arguments.AddRange(manifest.SystemIncludeDirectories.Select(include => "/external:I" + NativeBuildPaths.Resolve(manifestDirectory, include)));
        arguments.AddRange(manifest.CompilerArguments.Where(argument =>
            !argument.StartsWith("-std=", StringComparison.Ordinal) &&
            !argument.StartsWith("/std:", StringComparison.OrdinalIgnoreCase)));
        arguments.AddRange(manifest.SourceFiles.Select(source => NativeBuildPaths.Resolve(manifestDirectory, source)));
        arguments.Add("/link");
        arguments.Add("/OUT:" + artifactPath);
        arguments.AddRange(manifest.LibrarySearchDirectories.Select(directory =>
            "/LIBPATH:" + NativeBuildPaths.Resolve(manifestDirectory, directory)));
        arguments.AddRange(manifest.LinkLibraries.Select(library => NormalizeLibrary(manifestDirectory, library)));
        arguments.AddRange(manifest.LinkerArguments);
        return new(Name, [], [new("compile-and-link", compilerPath, arguments.AsReadOnly(), manifestDirectory)], artifactPath);
    }

    private static string NormalizeStandard(string standard) => standard.ToLowerInvariant() switch
    {
        "c++11" or "c++14" => "c++14",
        "c++17" => "c++17",
        "c++20" => "c++20",
        _ => "c++latest"
    };

    private static string NormalizeLibrary(string manifestDirectory, string library)
    {
        if (Path.IsPathRooted(library) || library.Contains('/') || library.Contains('\\'))
            return NativeBuildPaths.Resolve(manifestDirectory, library);
        return Path.HasExtension(library) ? library : library + ".lib";
    }
}
