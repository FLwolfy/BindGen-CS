namespace BGCS.Configuration;

using BGCS.Output;
using Microsoft.CodeAnalysis.CSharp;

internal static class CsCodeGeneratorConfigValidator
{
    internal static void Validate(CsCodeGeneratorConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        List<string> errors = [];
        if (config.ConfigVersion < 1 || config.ConfigVersion > CsCodeGeneratorConfig.CurrentConfigVersion)
        {
            errors.Add($"ConfigVersion {config.ConfigVersion} is unsupported. This BindGen-CS version accepts configuration versions 1 through {CsCodeGeneratorConfig.CurrentConfigVersion}.");
        }
        if (!SyntaxFacts.IsValidIdentifier(config.ApiName))
        {
            errors.Add("ApiName must be a valid C# identifier.");
        }
        if (!IsValidNamespace(config.Namespace))
        {
            errors.Add("Namespace must contain valid dot-separated C# identifiers.");
        }
        if (!config.UseFunctionTable && string.IsNullOrWhiteSpace(config.LibName))
        {
            errors.Add("LibName is required for DllImport and LibraryImport generation.");
        }
        if (config.EnableIncrementalCache && string.IsNullOrWhiteSpace(config.CacheDirectory))
        {
            errors.Add("CacheDirectory is required when incremental caching is enabled.");
        }
        if (config.PluginAssemblies == null)
        {
            errors.Add("PluginAssemblies cannot be null.");
        }
        else if (config.PluginAssemblies.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add("PluginAssemblies cannot contain an empty path.");
        }
        if (config.MergeGeneratedFilesToSingleFile)
        {
            try
            {
                SingleFileOutputNameResolver.Resolve(config);
            }
            catch (ArgumentException exception)
            {
                errors.Add(exception.Message);
            }
        }
        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Invalid BindGen-CS configuration:" + Environment.NewLine + "- " + string.Join(Environment.NewLine + "- ", errors));
        }
    }

    private static bool IsValidNamespace(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }
        return value.Split('.').All(SyntaxFacts.IsValidIdentifier);
    }
}
