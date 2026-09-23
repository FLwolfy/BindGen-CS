namespace BGCS.Configuration;

using BGCS.Output;
using Microsoft.CodeAnalysis.CSharp;

internal static class CsCodeGeneratorConfigValidator
{
    internal static void Validate(CsCodeGeneratorConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        List<string> errors = [];
        if (config.ConfigVersion != CsCodeGeneratorConfig.CurrentConfigVersion)
        {
            errors.Add($"ConfigVersion {config.ConfigVersion} is unsupported. This pre-release BindGen-CS build accepts only configuration version {CsCodeGeneratorConfig.CurrentConfigVersion}; no legacy migration is provided before the first stable release.");
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
        if (config.CSharpEmissionBackend != CSharpEmissionBackend.IntermediateRepresentation)
        {
            errors.Add($"CSharpEmissionBackend '{config.CSharpEmissionBackend}' is unsupported. The pre-release product is IR-native only.");
        }
        if (config.MergeGeneratedFilesToSingleFile || config.CSharpEmissionBackend == CSharpEmissionBackend.IntermediateRepresentation)
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
