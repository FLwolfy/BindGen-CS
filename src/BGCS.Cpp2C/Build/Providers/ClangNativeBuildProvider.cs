using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BGCS.Cpp2C.Build.Providers;

/// <summary>
/// Produces portable Clang/GNU-style shared-library build plans from bridge manifests.
/// The provider never invokes a command shell.
/// </summary>
public sealed class ClangNativeBuildProvider : INativeBuildProvider, INativeBuildPipelineProvider
{
    private readonly string m_compilerPath;
    /// <summary>
    /// Creates a native build provider with explicitly selected tool commands.
    /// </summary>
    /// <param name="compilerPath">Clang-compatible compiler command or absolute path.</param>
    public ClangNativeBuildProvider(string compilerPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(compilerPath);
        this.m_compilerPath = compilerPath;
    }

    /// <inheritdoc/>
    public string name => "clang-gnu-driver";

    /// <inheritdoc/>
    public NativeBuildPlan CreatePlan(
        CppBridgeBuildManifest manifest,
        string manifestPath,
        string? outputPath = null
    ) {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        string manifestDirectory = NativeBuildPaths.GetManifestDirectory(manifestPath);
        string artifactPath = NativeBuildPaths.GetOutputPath(manifest, manifestPath, outputPath);
        List<string> arguments = [];
        bool windows = manifest.targetIdentifier.StartsWith("windows-", StringComparison.OrdinalIgnoreCase);
        bool mac = manifest.targetIdentifier.StartsWith("macos-", StringComparison.OrdinalIgnoreCase);
        arguments.Add(mac ? "-dynamiclib" : "-shared");
        if (!windows)
            arguments.Add("-fPIC");
        arguments.Add("-std=" + manifest.languageStandard);
        if (!string.IsNullOrWhiteSpace(manifest.targetTriple) && NativeCompilerTargeting.AcceptsClangTarget(this.m_compilerPath))
            arguments.Add("--target=" + manifest.targetTriple);
        if (!string.IsNullOrWhiteSpace(manifest.targetSysRoot))
        {
            arguments.Add("--sysroot");
            arguments.Add(NativeBuildPaths.Resolve(manifestDirectory, manifest.targetSysRoot));
        }

        foreach (string define in manifest.defines)
            arguments.Add("-D" + define);
        foreach (string include in manifest.includeDirectories)
            arguments.Add("-I" + NativeBuildPaths.Resolve(manifestDirectory, include));
        foreach (string include in manifest.systemIncludeDirectories)
        {
            arguments.Add("-isystem");
            arguments.Add(NativeBuildPaths.Resolve(manifestDirectory, include));
        }

        arguments.AddRange(manifest.compilerArguments.Where(argument => !argument.StartsWith("-std=", StringComparison.Ordinal)));
        foreach (string source in manifest.sourceFiles)
            arguments.Add(NativeBuildPaths.Resolve(manifestDirectory, source));
        foreach (string searchDirectory in manifest.librarySearchDirectories)
            arguments.Add("-L" + NativeBuildPaths.Resolve(manifestDirectory, searchDirectory));
        foreach (string library in manifest.linkLibraries)
            arguments.Add(NormalizeLibraryArgument(manifestDirectory, library));
        arguments.AddRange(manifest.linkerArguments);
        arguments.Add("-o");
        arguments.Add(artifactPath);
        return new(this.name, this.m_compilerPath, arguments.AsReadOnly(), manifestDirectory, artifactPath);
    }

    /// <inheritdoc/>
    public NativeBuildPipeline CreatePipeline(
        CppBridgeBuildManifest manifest,
        string manifestPath,
        string? outputPath = null
    ) {
        NativeBuildPlan plan = CreatePlan(manifest, manifestPath, outputPath);
        return new(plan.provider, [], [new("compile-and-link", plan.executable, plan.arguments, plan.workingDirectory)], plan.outputFile);
    }

    private static string NormalizeLibraryArgument(
        string manifestDirectory,
        string library
    ) {
        if (string.IsNullOrWhiteSpace(library))
            throw new InvalidDataException("LinkLibraries cannot contain an empty value.");
        if (library.StartsWith("-", StringComparison.Ordinal))
            return library;
        if (Path.IsPathRooted(library) || library.Contains('/') || library.Contains('\\') || library.EndsWith(".a", StringComparison.OrdinalIgnoreCase) || library.EndsWith(".so", StringComparison.OrdinalIgnoreCase) || library.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase) || library.EndsWith(".lib", StringComparison.OrdinalIgnoreCase))
        {
            return NativeBuildPaths.Resolve(manifestDirectory, library);
        }

        return "-l" + library;
    }
}
