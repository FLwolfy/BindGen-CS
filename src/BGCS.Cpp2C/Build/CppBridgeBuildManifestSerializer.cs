using System.Text.Json;

namespace BGCS.Cpp2C.Build;

/// <summary>
/// Loads and validates versioned C++ bridge build manifests.
/// </summary>
public static class CppBridgeBuildManifestSerializer
{
    public static CppBridgeBuildManifest Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"C++ bridge build manifest not found: {fullPath}", fullPath);

        CppBridgeBuildManifest? manifest = JsonSerializer.Deserialize<CppBridgeBuildManifest>(
            File.ReadAllText(fullPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = false });
        if (manifest == null)
            throw new InvalidDataException($"Unable to deserialize C++ bridge build manifest '{fullPath}'.");
        if (manifest.ManifestVersion != CppBridgeBuildManifestEmitter.CurrentManifestVersion)
        {
            throw new InvalidDataException(
                $"Bridge manifest version {manifest.ManifestVersion} is unsupported. " +
                $"This version accepts manifest version {CppBridgeBuildManifestEmitter.CurrentManifestVersion}.");
        }
        ValidateRequired(manifest, fullPath);
        return manifest;
    }

    private static void ValidateRequired(CppBridgeBuildManifest manifest, string path)
    {
        List<string> missing = [];
        if (string.IsNullOrWhiteSpace(manifest.TargetIdentifier)) missing.Add(nameof(manifest.TargetIdentifier));
        if (string.IsNullOrWhiteSpace(manifest.LanguageStandard)) missing.Add(nameof(manifest.LanguageStandard));
        if (string.IsNullOrWhiteSpace(manifest.LibraryName)) missing.Add(nameof(manifest.LibraryName));
        if (manifest.SourceFiles == null || manifest.SourceFiles.Count == 0) missing.Add(nameof(manifest.SourceFiles));
        if (manifest.IncludeDirectories == null) missing.Add(nameof(manifest.IncludeDirectories));
        if (manifest.SystemIncludeDirectories == null) missing.Add(nameof(manifest.SystemIncludeDirectories));
        if (manifest.Defines == null) missing.Add(nameof(manifest.Defines));
        if (manifest.CompilerArguments == null) missing.Add(nameof(manifest.CompilerArguments));
        if (manifest.LibrarySearchDirectories == null) missing.Add(nameof(manifest.LibrarySearchDirectories));
        if (manifest.LinkLibraries == null) missing.Add(nameof(manifest.LinkLibraries));
        if (manifest.LinkerArguments == null) missing.Add(nameof(manifest.LinkerArguments));
        if (missing.Count > 0)
            throw new InvalidDataException($"Bridge manifest '{path}' is missing required fields: {string.Join(", ", missing)}.");
    }
}
