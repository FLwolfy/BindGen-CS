using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BGCS.Cpp2C.Build.Providers;

/// <summary>
/// Produces Windows DLL build pipelines for the clang-cl command-line interface.
/// </summary>
public sealed class ClangClNativeBuildProvider : INativeBuildPipelineProvider
{
    private readonly string m_compilerPath;
    /// <summary>
    /// Creates a native build provider with explicitly selected tool commands.
    /// </summary>
    /// <param name="compilerPath">Clang-cl command or absolute path.</param>
    public ClangClNativeBuildProvider(string compilerPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(compilerPath);
        this.m_compilerPath = compilerPath;
    }

    /// <inheritdoc/>
    public string name => "clang-cl";

    /// <inheritdoc/>
    public NativeBuildPipeline CreatePipeline(
        CppBridgeBuildManifest manifest,
        string manifestPath,
        string? outputPath = null
    ) {
        ArgumentNullException.ThrowIfNull(manifest);
        if (!manifest.targetIdentifier.StartsWith("windows-", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("clang-cl can only build Windows bridge targets.");
        string manifestDirectory = NativeBuildPaths.GetManifestDirectory(manifestPath);
        string artifactPath = NativeBuildPaths.GetOutputPath(manifest, manifestPath, outputPath);
        List<string> arguments = ["/nologo", "/LD", "/EHsc", "/std:" + NormalizeStandard(manifest.languageStandard)];
        if (!string.IsNullOrWhiteSpace(manifest.targetTriple))
            arguments.Add("/clang:--target=" + manifest.targetTriple);
        arguments.AddRange(manifest.defines.Select(define => "/D" + define));
        arguments.AddRange(manifest.includeDirectories.Select(include => "/I" + NativeBuildPaths.Resolve(manifestDirectory, include)));
        arguments.AddRange(manifest.systemIncludeDirectories.Select(include => "/external:I" + NativeBuildPaths.Resolve(manifestDirectory, include)));
        arguments.AddRange(manifest.compilerArguments.Where(argument => !argument.StartsWith("-std=", StringComparison.Ordinal) && !argument.StartsWith("/std:", StringComparison.OrdinalIgnoreCase)));
        arguments.AddRange(manifest.sourceFiles.Select(source => NativeBuildPaths.Resolve(manifestDirectory, source)));
        arguments.Add("/link");
        arguments.Add("/OUT:" + artifactPath);
        arguments.AddRange(manifest.librarySearchDirectories.Select(directory => "/LIBPATH:" + NativeBuildPaths.Resolve(manifestDirectory, directory)));
        arguments.AddRange(manifest.linkLibraries.Select(library => NormalizeLibrary(manifestDirectory, library)));
        arguments.AddRange(manifest.linkerArguments);
        return new(this.name, [], [new("compile-and-link", this.m_compilerPath, arguments.AsReadOnly(), manifestDirectory)], artifactPath);
    }

    private static string NormalizeStandard(string standard) => standard.ToLowerInvariant() switch
    {
        "c++11" or "c++14" => "c++14",
        "c++17" => "c++17",
        "c++20" => "c++20",
        _ => "c++latest"
    };
    private static string NormalizeLibrary(
        string manifestDirectory,
        string library
    ) {
        if (Path.IsPathRooted(library) || library.Contains('/') || library.Contains('\\'))
            return NativeBuildPaths.Resolve(manifestDirectory, library);
        return Path.HasExtension(library) ? library : library + ".lib";
    }
}
