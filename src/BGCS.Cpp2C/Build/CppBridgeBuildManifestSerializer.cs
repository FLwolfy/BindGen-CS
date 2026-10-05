using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BGCS.Cpp2C.Build;

/// <summary>
/// Loads the current C++ bridge build contract and rejects incomplete compiler inputs.
/// </summary>
public static class CppBridgeBuildManifestSerializer
{
    /// <summary>
    /// Reads the current bridge manifest and validates required target, source and link inputs.
    /// </summary>
    /// <param name="path">Manifest file to load.</param>
    /// <returns>The validated compiler contract; relative paths remain relative to this manifest.</returns>
    /// <exception cref="FileNotFoundException">The manifest does not exist.</exception>
    /// <exception cref="InvalidDataException">The document is empty or required compiler inputs are missing.</exception>
    /// <exception cref="JsonException">The document is not valid JSON for the current contract.</exception>
    public static CppBridgeBuildManifest Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"C++ bridge build manifest not found: {fullPath}", fullPath);
        CppBridgeBuildManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<CppBridgeBuildManifest>(File.ReadAllText(fullPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = false });
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException($"Bridge manifest '{fullPath}' contains missing or invalid compiler inputs.", exception);
        }
        if (manifest == null)
            throw new InvalidDataException($"Unable to deserialize C++ bridge build manifest '{fullPath}'.");
        ValidateRequired(manifest, fullPath);
        return manifest;
    }

    private static void ValidateRequired(
        CppBridgeBuildManifest manifest,
        string path
    ) {
        List<string> missing = [];
        if (string.IsNullOrWhiteSpace(manifest.targetIdentifier))
            missing.Add(nameof(manifest.targetIdentifier));
        if (string.IsNullOrWhiteSpace(manifest.languageStandard))
            missing.Add(nameof(manifest.languageStandard));
        if (string.IsNullOrWhiteSpace(manifest.libraryName))
            missing.Add(nameof(manifest.libraryName));
        if (manifest.sourceFiles == null || manifest.sourceFiles.Count == 0)
            missing.Add(nameof(manifest.sourceFiles));
        if (manifest.publicHeaderFiles == null)
            missing.Add(nameof(manifest.publicHeaderFiles));
        if (manifest.originalHeaderFiles == null)
            missing.Add(nameof(manifest.originalHeaderFiles));
        if (manifest.includeDirectories == null)
            missing.Add(nameof(manifest.includeDirectories));
        if (manifest.systemIncludeDirectories == null)
            missing.Add(nameof(manifest.systemIncludeDirectories));
        if (manifest.defines == null)
            missing.Add(nameof(manifest.defines));
        if (manifest.compilerArguments == null)
            missing.Add(nameof(manifest.compilerArguments));
        if (manifest.librarySearchDirectories == null)
            missing.Add(nameof(manifest.librarySearchDirectories));
        if (manifest.linkLibraries == null)
            missing.Add(nameof(manifest.linkLibraries));
        if (manifest.linkerArguments == null)
            missing.Add(nameof(manifest.linkerArguments));
        if (missing.Count > 0)
            throw new InvalidDataException($"Bridge manifest '{path}' is missing required fields: {string.Join(", ", missing)}.");
    }
}
