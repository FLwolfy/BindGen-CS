namespace BGCS.Configuration;

using BGCS.Core.Mapping;
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
        ValidateExternalTypeContracts(config, errors);
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

    private static void ValidateExternalTypeContracts(CsCodeGeneratorConfig config, List<string> errors)
    {
        if (config.TypeMappings == null)
        {
            errors.Add("TypeMappings cannot be null.");
            return;
        }
        if (config.ExternalTypeContracts == null)
        {
            errors.Add("ExternalTypeContracts cannot be null.");
            return;
        }

        HashSet<string> nativeSelectors = new(StringComparer.Ordinal);
        HashSet<string> managedSelectors = new(StringComparer.Ordinal);
        foreach (ExternalTypeContract? contract in config.ExternalTypeContracts)
        {
            if (contract == null)
            {
                errors.Add("ExternalTypeContracts cannot contain null entries.");
                continue;
            }
            if (contract.NativeTypes == null || contract.NativeTypes.Count == 0)
                errors.Add("ExternalTypeContracts entries require at least one NativeTypes value.");
            else
            {
                foreach (string nativeType in contract.NativeTypes)
                {
                    if (string.IsNullOrWhiteSpace(nativeType))
                    {
                        errors.Add("ExternalTypeContracts NativeTypes cannot contain an empty value.");
                        continue;
                    }
                    if (!nativeSelectors.Add(nativeType))
                        errors.Add($"ExternalTypeContracts contains duplicate native selector '{nativeType}'.");
                }
            }
            if (contract.ManagedTypes == null || contract.ManagedTypes.Count == 0)
                errors.Add("ExternalTypeContracts entries require at least one ManagedTypes value.");
            else
            {
                foreach (string managedType in contract.ManagedTypes)
                {
                    if (string.IsNullOrWhiteSpace(managedType))
                    {
                        errors.Add("ExternalTypeContracts ManagedTypes cannot contain an empty value.");
                        continue;
                    }
                    if (!managedSelectors.Add(managedType))
                        errors.Add($"ExternalTypeContracts contains duplicate managed selector '{managedType}'.");
                }
            }
            string contractName = contract.ManagedTypes?.FirstOrDefault() ?? "<missing>";
            if (contract.Size <= 0)
                errors.Add($"External type contract '{contractName}' requires a positive Size.");
            if (contract.Alignment <= 0 || (contract.Alignment & (contract.Alignment - 1)) != 0)
                errors.Add($"External type contract '{contractName}' Alignment must be a positive power of two.");
            if (!Enum.IsDefined(contract.ByValuePolicy))
                errors.Add($"External type contract '{contractName}' has an invalid ByValuePolicy.");

            if (contract.NativeTypes?.All(value => !string.IsNullOrWhiteSpace(value)) == true &&
                contract.ManagedTypes?.All(value => !string.IsNullOrWhiteSpace(value)) == true)
            {
                KeyValuePair<string, string>[] selectedMappings = config.TypeMappings
                    .Where(mapping => contract.NativeTypes.Any(selector =>
                        ExternalTypeContract.MatchesSelector(selector, mapping.Key)))
                    .ToArray();
                if (selectedMappings.Length == 0)
                {
                    errors.Add($"External type contract '{contractName}' does not select any TypeMappings entry.");
                }
                foreach ((string nativeType, string managedType) in selectedMappings)
                {
                    if (!contract.ManagedTypes.Any(selector =>
                            ExternalTypeContract.MatchesSelector(selector, managedType)))
                    {
                        errors.Add($"External type contract '{contractName}' selects native type '{nativeType}', but its TypeMappings value '{managedType}' is not selected by ManagedTypes.");
                    }
                }
                foreach (string managedSelector in contract.ManagedTypes)
                {
                    if (!selectedMappings.Any(mapping =>
                            ExternalTypeContract.MatchesSelector(managedSelector, mapping.Value)))
                    {
                        errors.Add($"External managed selector '{managedSelector}' does not match any TypeMappings value selected by contract '{contractName}'.");
                    }
                }
            }
        }

        foreach ((string nativeType, string managedType) in config.TypeMappings)
        {
            int matches = config.ExternalTypeContracts.Count(contract => contract != null &&
                contract.NativeTypes != null && contract.ManagedTypes != null &&
                contract.Matches(nativeType, managedType));
            if (matches > 1)
                errors.Add($"TypeMappings entry '{nativeType}' -> '{managedType}' matches more than one ExternalTypeContracts entry.");
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
