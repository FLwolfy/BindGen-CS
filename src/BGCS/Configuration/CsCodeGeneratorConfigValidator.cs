using System;
using System.Collections.Generic;
using System.Linq;

namespace BGCS.Configuration;

using BGCS.Configuration.Mapping;
using BGCS.Output;
using Microsoft.CodeAnalysis.CSharp;

internal static class CsCodeGeneratorConfigValidator
{
    internal static void Validate(CsCodeGeneratorConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        List<string> errors = [];

        if (!SyntaxFacts.IsValidIdentifier(config.apiName))
        {
            errors.Add("ApiName must be a valid C# identifier.");
        }

        if (!IsValidNamespace(config.@namespace))
        {
            errors.Add("Namespace must contain valid dot-separated C# identifiers.");
        }

        if (!config.useFunctionTable && string.IsNullOrWhiteSpace(config.libName))
        {
            errors.Add("LibName is required for DllImport and LibraryImport generation.");
        }

        if (config.enableIncrementalCache && string.IsNullOrWhiteSpace(config.cacheDirectory))
        {
            errors.Add("CacheDirectory is required when incremental caching is enabled.");
        }

        if (config.pluginAssemblies == null)
        {
            errors.Add("PluginAssemblies cannot be null.");
        }
        else if (config.pluginAssemblies.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add("PluginAssemblies cannot contain an empty path.");
        }

        ValidateExternalTypeContracts(config, errors);

        if (!string.IsNullOrWhiteSpace(config.singleFileOutputName))
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

    private static void ValidateExternalTypeContracts(
        CsCodeGeneratorConfig config,
        List<string> errors
    ) {
        if (config.typeMappings == null)
        {
            errors.Add("TypeMappings cannot be null.");
            return;
        }

        if (config.externalTypeContracts == null)
        {
            errors.Add("ExternalTypeContracts cannot be null.");
            return;
        }

        HashSet<string> nativeSelectors = new(StringComparer.Ordinal);
        HashSet<string> managedSelectors = new(StringComparer.Ordinal);
        foreach (ExternalTypeContract? contract in config.externalTypeContracts)
        {
            if (contract == null)
            {
                errors.Add("ExternalTypeContracts cannot contain null entries.");
                continue;
            }

            if (contract.nativeTypes == null || contract.nativeTypes.Count == 0)
                errors.Add("ExternalTypeContracts entries require at least one NativeTypes value.");
            else
            {
                foreach (string nativeType in contract.nativeTypes)
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

            if (contract.managedTypes == null || contract.managedTypes.Count == 0)
                errors.Add("ExternalTypeContracts entries require at least one ManagedTypes value.");
            else
            {
                foreach (string managedType in contract.managedTypes)
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

            string contractName = contract.managedTypes?.FirstOrDefault() ?? "<missing>";
            if (contract.size <= 0)
                errors.Add($"External type contract '{contractName}' requires a positive Size.");
            if (contract.alignment <= 0 || (contract.alignment & (contract.alignment - 1)) != 0)
                errors.Add($"External type contract '{contractName}' Alignment must be a positive power of two.");
            if (!Enum.IsDefined(contract.byValuePolicy))
                errors.Add($"External type contract '{contractName}' has an invalid ByValuePolicy.");
            if (contract.nativeTypes?.All(value => !string.IsNullOrWhiteSpace(value)) == true && contract.managedTypes?.All(value => !string.IsNullOrWhiteSpace(value)) == true)
            {
                KeyValuePair<string, string>[] selectedMappings = config.typeMappings.Where(mapping => contract.nativeTypes.Any(selector => ExternalTypeContract.MatchesSelector(selector, mapping.Key))).ToArray();
                if (selectedMappings.Length == 0)
                {
                    errors.Add($"External type contract '{contractName}' does not select any TypeMappings entry.");
                }

                foreach ((string nativeType, string managedType) in selectedMappings)
                {
                    if (!contract.managedTypes.Any(selector => ExternalTypeContract.MatchesSelector(selector, managedType)))
                    {
                        errors.Add($"External type contract '{contractName}' selects native type '{nativeType}', but its TypeMappings value '{managedType}' is not selected by ManagedTypes.");
                    }
                }

                foreach (string managedSelector in contract.managedTypes)
                {
                    if (!selectedMappings.Any(mapping => ExternalTypeContract.MatchesSelector(managedSelector, mapping.Value)))
                    {
                        errors.Add($"External managed selector '{managedSelector}' does not match any TypeMappings value selected by contract '{contractName}'.");
                    }
                }
            }
        }

        foreach ((string nativeType, string managedType) in config.typeMappings)
        {
            int matches = config.externalTypeContracts.Count(contract => contract != null && contract.nativeTypes != null && contract.managedTypes != null && contract.Matches(nativeType, managedType));
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
