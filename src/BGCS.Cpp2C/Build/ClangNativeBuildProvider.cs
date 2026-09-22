namespace BGCS.Cpp2C.Build;

/// <summary>
/// Produces portable Clang/GNU-style shared-library build plans from bridge manifests.
/// The provider never invokes a command shell.
/// </summary>
public sealed class ClangNativeBuildProvider : INativeBuildProvider, INativeBuildPipelineProvider
{
    private readonly string compilerPath;

    public ClangNativeBuildProvider(string compilerPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(compilerPath);
        this.compilerPath = compilerPath;
    }

    public string Name => "clang-gnu-driver";

    public NativeBuildPlan CreatePlan(CppBridgeBuildManifest manifest, string manifestPath, string? outputPath = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        string manifestDirectory = NativeBuildPaths.GetManifestDirectory(manifestPath);
        string artifactPath = NativeBuildPaths.GetOutputPath(manifest, manifestPath, outputPath);

        List<string> arguments = [];
        bool windows = manifest.TargetIdentifier.StartsWith("windows-", StringComparison.OrdinalIgnoreCase);
        bool mac = manifest.TargetIdentifier.StartsWith("macos-", StringComparison.OrdinalIgnoreCase);
        arguments.Add(mac ? "-dynamiclib" : "-shared");
        if (!windows)
            arguments.Add("-fPIC");
        arguments.Add("-std=" + manifest.LanguageStandard);
        if (!string.IsNullOrWhiteSpace(manifest.TargetTriple))
            arguments.Add("--target=" + manifest.TargetTriple);
        if (!string.IsNullOrWhiteSpace(manifest.TargetSysRoot))
        {
            arguments.Add("--sysroot");
            arguments.Add(NativeBuildPaths.Resolve(manifestDirectory, manifest.TargetSysRoot));
        }
        foreach (string define in manifest.Defines)
            arguments.Add("-D" + define);
        foreach (string include in manifest.IncludeDirectories)
            arguments.Add("-I" + NativeBuildPaths.Resolve(manifestDirectory, include));
        foreach (string include in manifest.SystemIncludeDirectories)
        {
            arguments.Add("-isystem");
            arguments.Add(NativeBuildPaths.Resolve(manifestDirectory, include));
        }
        arguments.AddRange(manifest.CompilerArguments.Where(argument =>
            !argument.StartsWith("-std=", StringComparison.Ordinal)));
        foreach (string source in manifest.SourceFiles)
            arguments.Add(NativeBuildPaths.Resolve(manifestDirectory, source));
        foreach (string searchDirectory in manifest.LibrarySearchDirectories)
            arguments.Add("-L" + NativeBuildPaths.Resolve(manifestDirectory, searchDirectory));
        foreach (string library in manifest.LinkLibraries)
            arguments.Add(NormalizeLibraryArgument(manifestDirectory, library));
        arguments.AddRange(manifest.LinkerArguments);
        arguments.Add("-o");
        arguments.Add(artifactPath);

        return new(Name, compilerPath, arguments.AsReadOnly(), manifestDirectory, artifactPath);
    }

    public NativeBuildPipeline CreatePipeline(CppBridgeBuildManifest manifest, string manifestPath, string? outputPath = null)
    {
        NativeBuildPlan plan = CreatePlan(manifest, manifestPath, outputPath);
        return new(
            plan.Provider,
            [],
            [new("compile-and-link", plan.Executable, plan.Arguments, plan.WorkingDirectory)],
            plan.OutputFile);
    }

    private static string NormalizeLibraryArgument(string manifestDirectory, string library)
    {
        if (string.IsNullOrWhiteSpace(library))
            throw new InvalidDataException("LinkLibraries cannot contain an empty value.");
        if (library.StartsWith("-", StringComparison.Ordinal))
            return library;
        if (Path.IsPathRooted(library) || library.Contains('/') || library.Contains('\\') ||
            library.EndsWith(".a", StringComparison.OrdinalIgnoreCase) ||
            library.EndsWith(".so", StringComparison.OrdinalIgnoreCase) ||
            library.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase) ||
            library.EndsWith(".lib", StringComparison.OrdinalIgnoreCase))
        {
            return NativeBuildPaths.Resolve(manifestDirectory, library);
        }
        return "-l" + library;
    }

}
