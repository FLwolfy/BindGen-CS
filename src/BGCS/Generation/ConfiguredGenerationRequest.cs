using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BGCS.Configuration;

namespace BGCS.Generation;

internal sealed record ConfiguredGenerationRequest(
    string baseDirectory,
    IReadOnlyList<string> headerFiles,
    IReadOnlyList<string>? allowedHeaders,
    string outputPath
);
internal static class ConfiguredGenerationRequestResolver
{
    internal static ConfiguredGenerationRequest Resolve(
        CsCodeGeneratorConfig config,
        string? outputPath
    ) {
        ArgumentNullException.ThrowIfNull(config);
        if (config.entryFiles is not { Count: > 0 })
        {
            throw new InvalidOperationException("EntryFiles must contain at least one header file.");
        }

        string baseDirectory = config.configDirectory ?? Environment.CurrentDirectory;
        List<string> headerFiles = config.entryFiles.Select(path => ResolvePath(baseDirectory, path)).ToList();
        List<string>? allowedHeaders = config.allowedHeaders is not { Count: > 0 } ? null : config.allowedHeaders.Select(path => ResolvePath(baseDirectory, path)).ToList();
        string configuredOutputPath = string.IsNullOrWhiteSpace(outputPath) ? config.outputPath : outputPath;
        if (string.IsNullOrWhiteSpace(configuredOutputPath))
        {
            configuredOutputPath = "Generated";
        }

        return new(baseDirectory, headerFiles, allowedHeaders, ResolvePath(baseDirectory, configuredOutputPath));
    }

    private static string ResolvePath(
        string baseDirectory,
        string path
    ) {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Configured paths cannot be empty.", nameof(path));
        }

        return Path.GetFullPath(path, baseDirectory);
    }
}
