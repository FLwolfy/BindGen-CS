using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BGCS.Cpp2C.Configuration;

namespace BGCS.Cpp2C.Build;

/// <summary>
/// Emits the deterministic build contract consumed by native build providers.
/// </summary>
public static class CppBridgeBuildManifestEmitter
{
    /// <summary>
    /// Writes the current target-specific compiler contract beside staged bridge sources.
    /// </summary>
    /// <param name="config">Resolved native target, compiler and link configuration.</param>
    /// <param name="originalHeaders">Source headers required to compile the facade.</param>
    /// <param name="outputPath">Candidate bridge directory containing generated headers and sources.</param>
    /// <returns>The written manifest path; publication remains the caller's responsibility.</returns>
    /// <exception cref="ArgumentNullException">Configuration or original headers are null.</exception>
    /// <exception cref="ArgumentException">The output path is empty.</exception>
    public static string Emit(
        Cpp2CGeneratorConfig config,
        IReadOnlyList<string> originalHeaders,
        string outputPath
    ) {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(originalHeaders);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        string outputRoot = Path.GetFullPath(outputPath);
        string configRoot = config.configDirectory ?? Environment.CurrentDirectory;
        string manifestPath = Path.Combine(outputRoot, config.buildManifestFileName);
        string? languageStandardArgument = config.additionalArguments.LastOrDefault(argument => argument.StartsWith("-std=", StringComparison.Ordinal));
        string languageStandard = languageStandardArgument == null ? config.languageStandard : languageStandardArgument["-std=".Length..];
        string[] sourceFiles = EnumerateRelative(outputRoot, "*.cpp");
        string[] publicHeaders = EnumerateRelative(outputRoot, "*.h");
        string[] originalHeaderFiles = originalHeaders.Select(path => MakeManifestPath(outputRoot, Path.GetFullPath(path, configRoot))).Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        string[] includeDirectories = new[]
        {
            Path.Combine(outputRoot, "include")
        }.Concat(publicHeaders.Select(path => Path.GetDirectoryName(Path.GetFullPath(path, outputRoot))!)).Concat(originalHeaders.Select(path => Path.GetDirectoryName(Path.GetFullPath(path, configRoot))!)).Concat(config.includeFolders.Select(path => Path.GetFullPath(path, configRoot))).Select(path => MakeManifestPath(outputRoot, path)).Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        string[] systemIncludeDirectories = config.systemIncludeFolders.Select(path => MakeManifestPath(outputRoot, Path.GetFullPath(path, configRoot))).Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        string[] librarySearchDirectories = config.librarySearchFolders.Select(path => MakeManifestPath(outputRoot, Path.GetFullPath(path, configRoot))).Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        BGCS.Core.Targeting.NativeTargetDescriptor resolvedTarget = config.resolvedTarget;
        CppBridgeBuildManifest manifest = new(resolvedTarget.targetId.value, resolvedTarget.triple, MakeOptionalManifestPath(outputRoot, configRoot, config.targetSysRoot ?? resolvedTarget.toolchain.sysRoot), NormalizeCompilerPath(outputRoot, configRoot, config.compilerPath ?? resolvedTarget.toolchain.compilerPath), languageStandard, config.nativeLibraryName, sourceFiles, publicHeaders, originalHeaderFiles, includeDirectories, systemIncludeDirectories, config.defines.Append(config.namePrefix + "BUILD_SHARED").Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(), config.additionalArguments.ToArray(), librarySearchDirectories, config.linkLibraries.Select(library => NormalizeLinkLibrary(outputRoot, configRoot, library)).ToArray(), config.linkerArguments.ToArray());
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        return manifestPath;
    }

    private static string[] EnumerateRelative(
        string outputRoot,
        string pattern
    ) => Directory.GetFiles(outputRoot, pattern, SearchOption.AllDirectories).Select(path => MakeManifestPath(outputRoot, path)).OrderBy(path => path, StringComparer.Ordinal).ToArray();
    private static string? MakeOptionalManifestPath(
        string outputRoot,
        string configRoot,
        string? path
    ) {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        return MakeManifestPath(outputRoot, Path.GetFullPath(path, configRoot));
    }

    private static string? NormalizeCompilerPath(
        string outputRoot,
        string configRoot,
        string? path
    ) {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        if (!Path.IsPathRooted(path) && !path.Contains('/') && !path.Contains('\\'))
            return path;
        return MakeManifestPath(outputRoot, Path.GetFullPath(path, configRoot));
    }

    private static string NormalizeLinkLibrary(
        string outputRoot,
        string configRoot,
        string library
    ) {
        if (string.IsNullOrWhiteSpace(library) || library.StartsWith("-", StringComparison.Ordinal))
            return library;
        if (Path.IsPathRooted(library) || library.Contains('/') || library.Contains('\\') || library.EndsWith(".a", StringComparison.OrdinalIgnoreCase) || library.EndsWith(".so", StringComparison.OrdinalIgnoreCase) || library.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase) || library.EndsWith(".lib", StringComparison.OrdinalIgnoreCase))
        {
            return MakeManifestPath(outputRoot, Path.GetFullPath(library, configRoot));
        }

        return library;
    }

    private static string MakeManifestPath(
        string outputRoot,
        string fullPath
    ) {
        string relativePath = Path.GetRelativePath(outputRoot, fullPath);
        string result = Path.IsPathRooted(relativePath) ? fullPath : relativePath;
        return result.Replace('\\', '/');
    }
}
