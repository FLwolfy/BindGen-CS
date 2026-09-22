using System.Text.Json;

namespace BGCS.Cpp2C.Build;

/// <summary>
/// Emits the deterministic build contract consumed by native build providers.
/// </summary>
public static class CppBridgeBuildManifestEmitter
{
    public const int CurrentManifestVersion = 1;

    public static string Emit(Cpp2CGeneratorConfig config, IReadOnlyList<string> originalHeaders, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(originalHeaders);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        string outputRoot = Path.GetFullPath(outputPath);
        string configRoot = config.ConfigDirectory ?? Environment.CurrentDirectory;
        string manifestPath = Path.Combine(outputRoot, config.BuildManifestFileName);
        string? languageStandardArgument = config.AdditionalArguments
            .LastOrDefault(argument => argument.StartsWith("-std=", StringComparison.Ordinal));
        string languageStandard = languageStandardArgument == null
            ? config.LanguageStandard
            : languageStandardArgument["-std=".Length..];

        string[] sourceFiles = EnumerateRelative(outputRoot, "*.cpp");
        string[] publicHeaders = EnumerateRelative(outputRoot, "*.h");
        string[] originalHeaderFiles = originalHeaders
            .Select(path => MakeManifestPath(outputRoot, Path.GetFullPath(path, configRoot)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        string[] includeDirectories = new[] { Path.Combine(outputRoot, "include") }
            .Concat(originalHeaders.Select(path => Path.GetDirectoryName(Path.GetFullPath(path, configRoot))!))
            .Concat(config.IncludeFolders.Select(path => Path.GetFullPath(path, configRoot)))
            .Select(path => MakeManifestPath(outputRoot, path))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        string[] systemIncludeDirectories = config.SystemIncludeFolders
            .Select(path => MakeManifestPath(outputRoot, Path.GetFullPath(path, configRoot)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        string[] librarySearchDirectories = config.LibrarySearchFolders
            .Select(path => MakeManifestPath(outputRoot, Path.GetFullPath(path, configRoot)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        BGCS.CppAst.Targeting.CppTarget resolvedTarget = config.ResolvedTarget;
        CppBridgeBuildManifest manifest = new(
            CurrentManifestVersion,
            resolvedTarget.Identifier,
            resolvedTarget.Triple,
            MakeOptionalManifestPath(outputRoot, configRoot, config.TargetSysRoot),
            NormalizeCompilerPath(outputRoot, configRoot, config.CompilerPath),
            languageStandard,
            config.NativeLibraryName,
            sourceFiles,
            publicHeaders,
            originalHeaderFiles,
            includeDirectories,
            systemIncludeDirectories,
            config.Defines.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            config.AdditionalArguments.ToArray(),
            librarySearchDirectories,
            config.LinkLibraries.Select(library => NormalizeLinkLibrary(outputRoot, configRoot, library)).ToArray(),
            config.LinkerArguments.ToArray());

        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true
        }) + Environment.NewLine);
        return manifestPath;
    }

    private static string[] EnumerateRelative(string outputRoot, string pattern) =>
        Directory.GetFiles(outputRoot, pattern, SearchOption.AllDirectories)
            .Select(path => MakeManifestPath(outputRoot, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    private static string? MakeOptionalManifestPath(string outputRoot, string configRoot, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        return MakeManifestPath(outputRoot, Path.GetFullPath(path, configRoot));
    }

    private static string? NormalizeCompilerPath(string outputRoot, string configRoot, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        if (!Path.IsPathRooted(path) && !path.Contains('/') && !path.Contains('\\'))
            return path;
        return MakeManifestPath(outputRoot, Path.GetFullPath(path, configRoot));
    }

    private static string NormalizeLinkLibrary(string outputRoot, string configRoot, string library)
    {
        if (string.IsNullOrWhiteSpace(library) || library.StartsWith("-", StringComparison.Ordinal))
            return library;
        if (Path.IsPathRooted(library) || library.Contains('/') || library.Contains('\\') ||
            library.EndsWith(".a", StringComparison.OrdinalIgnoreCase) ||
            library.EndsWith(".so", StringComparison.OrdinalIgnoreCase) ||
            library.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase) ||
            library.EndsWith(".lib", StringComparison.OrdinalIgnoreCase))
        {
            return MakeManifestPath(outputRoot, Path.GetFullPath(library, configRoot));
        }
        return library;
    }

    private static string MakeManifestPath(string outputRoot, string fullPath)
    {
        string relativePath = Path.GetRelativePath(outputRoot, fullPath);
        string result = Path.IsPathRooted(relativePath) ? fullPath : relativePath;
        return result.Replace('\\', '/');
    }
}
