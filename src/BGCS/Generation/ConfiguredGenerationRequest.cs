namespace BGCS.Generation;

internal sealed record ConfiguredGenerationRequest(
    string BaseDirectory,
    IReadOnlyList<string> HeaderFiles,
    IReadOnlyList<string>? AllowedHeaders,
    string OutputPath);

internal static class ConfiguredGenerationRequestResolver
{
    internal static ConfiguredGenerationRequest Resolve(CsCodeGeneratorConfig config, string? outputPath)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config.EntryFiles is not { Count: > 0 })
        {
            throw new InvalidOperationException("EntryFiles must contain at least one header file.");
        }

        string baseDirectory = config.ConfigDirectory ?? Environment.CurrentDirectory;
        List<string> headerFiles = config.EntryFiles.Select(path => ResolvePath(baseDirectory, path)).ToList();
        List<string>? allowedHeaders = config.AllowedHeaders is not { Count: > 0 }
            ? null
            : config.AllowedHeaders.Select(path => ResolvePath(baseDirectory, path)).ToList();
        string configuredOutputPath = string.IsNullOrWhiteSpace(outputPath) ? config.OutputPath : outputPath;
        if (string.IsNullOrWhiteSpace(configuredOutputPath))
        {
            configuredOutputPath = "Generated";
        }

        return new(baseDirectory, headerFiles, allowedHeaders, ResolvePath(baseDirectory, configuredOutputPath));
    }

    private static string ResolvePath(string baseDirectory, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Configured paths cannot be empty.", nameof(path));
        }
        return Path.GetFullPath(path, baseDirectory);
    }
}
