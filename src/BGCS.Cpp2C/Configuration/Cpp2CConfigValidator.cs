namespace BGCS.Cpp2C.Configuration;

/// <summary>
/// Validates C++ bridge configuration before parsing or replacing generated output.
/// </summary>
public static class Cpp2CConfigValidator
{
    public static void Validate(Cpp2CGeneratorConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        List<string> errors = [];
        if (config.ConfigVersion != Cpp2CGeneratorConfig.CurrentConfigVersion)
        {
            errors.Add($"ConfigVersion {config.ConfigVersion} is unsupported. This pre-release BGCS.Cpp2C build accepts only configuration version {Cpp2CGeneratorConfig.CurrentConfigVersion}; no legacy migration is provided before the first stable release.");
        }
        if (string.IsNullOrWhiteSpace(config.LanguageStandard))
            errors.Add("LanguageStandard is required.");
        if (config.EnableIncrementalCache && string.IsNullOrWhiteSpace(config.CacheDirectory))
            errors.Add("CacheDirectory is required when incremental caching is enabled.");
        if (config.PluginAssemblies == null)
            errors.Add("PluginAssemblies cannot be null.");
        else if (config.PluginAssemblies.Any(string.IsNullOrWhiteSpace))
            errors.Add("PluginAssemblies cannot contain an empty path.");
        if (config.GenerateBuildManifest)
        {
            if (string.IsNullOrWhiteSpace(config.BuildManifestFileName) ||
                config.BuildManifestFileName.Contains('/') ||
                config.BuildManifestFileName.Contains('\\') ||
                !string.Equals(config.BuildManifestFileName, Path.GetFileName(config.BuildManifestFileName), StringComparison.Ordinal) ||
                !config.BuildManifestFileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("BuildManifestFileName must be a .json file name without a directory component.");
            }
        }
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Invalid BGCS.Cpp2C configuration:" + Environment.NewLine + "- " +
                string.Join(Environment.NewLine + "- ", errors));
        }
    }
}
