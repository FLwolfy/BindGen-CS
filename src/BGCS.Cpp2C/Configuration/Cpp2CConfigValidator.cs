using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BGCS.Cpp2C.Configuration;

using System.Text.RegularExpressions;

/// <summary>
/// Validates C++ bridge configuration before parsing or replacing generated output.
/// </summary>
public static class Cpp2CConfigValidator
{
    /// <summary>
    /// Validates bridge generation, declarative lowering and output settings before parsing starts.
    /// </summary>
    /// <param name="config">Configuration to validate; the instance is not changed.</param>
    /// <exception cref="ArgumentNullException">The configuration is null.</exception>
    /// <exception cref="InvalidOperationException">One or more bridge settings are invalid.</exception>
    public static void Validate(Cpp2CGeneratorConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        List<string> errors = [];

        if (string.IsNullOrWhiteSpace(config.languageStandard))
            errors.Add("LanguageStandard is required.");
        if (!Enum.IsDefined(config.cSharpStrictSafetySeverity))
            errors.Add("CSharpStrictSafetySeverity is invalid.");
        if (config.enableIncrementalCache && string.IsNullOrWhiteSpace(config.cacheDirectory))
            errors.Add("CacheDirectory is required when incremental caching is enabled.");
        if (config.pluginAssemblies == null)
            errors.Add("PluginAssemblies cannot be null.");
        else if (config.pluginAssemblies.Any(string.IsNullOrWhiteSpace))
            errors.Add("PluginAssemblies cannot contain an empty path.");
        ValidateLoweringRecipes(config, errors);
        if (config.generateBuildManifest)
        {
            if (string.IsNullOrWhiteSpace(config.buildManifestFileName) || config.buildManifestFileName.Contains('/') || config.buildManifestFileName.Contains('\\') || !string.Equals(config.buildManifestFileName, Path.GetFileName(config.buildManifestFileName), StringComparison.Ordinal) || !config.buildManifestFileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("BuildManifestFileName must be a .json file name without a directory component.");
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Invalid BGCS.Cpp2C configuration:" + Environment.NewLine + "- " + string.Join(Environment.NewLine + "- ", errors));
        }
    }

    private static void ValidateLoweringRecipes(
        Cpp2CGeneratorConfig config,
        List<string> errors
    ) {
        if (config.typeLowerings == null)
            errors.Add("TypeLowerings cannot be null.");
        else
        {
            foreach (var recipe in config.typeLowerings)
            {
                if (recipe == null)
                {
                    errors.Add("TypeLowerings cannot contain null entries.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(recipe.name))
                    errors.Add("TypeLowerings entries require a non-empty Name.");
                if (string.IsNullOrWhiteSpace(recipe.typePattern))
                    errors.Add($"Type lowering '{recipe.name}' requires TypePattern.");
                if (string.IsNullOrWhiteSpace(recipe.cAbiType))
                    errors.Add($"Type lowering '{recipe.name}' requires CAbiType.");
                if (!Enum.IsDefined(recipe.safety))
                    errors.Add($"Type lowering '{recipe.name}' has an invalid Safety value.");
                if (!Enum.IsDefined(recipe.abiShape))
                    errors.Add($"Type lowering '{recipe.name}' has an invalid AbiShape value.");
                if (recipe.abiParameters == null)
                    errors.Add($"Type lowering '{recipe.name}' AbiParameters cannot be null.");
                else
                {
                    HashSet<string> suffixes = new(StringComparer.Ordinal);
                    foreach (var parameter in recipe.abiParameters)
                    {
                        if (parameter == null || !Regex.IsMatch(parameter.nameSuffix, "^[_A-Za-z0-9]*$", RegexOptions.CultureInvariant) || string.IsNullOrWhiteSpace(parameter.cAbiType) || !suffixes.Add(parameter.nameSuffix))
                        {
                            errors.Add($"Type lowering '{recipe.name}' contains invalid or duplicate AbiParameters metadata.");
                            break;
                        }
                    }
                }

                if (recipe.requiredHeaders == null)
                    errors.Add($"Type lowering '{recipe.name}' RequiredHeaders cannot be null.");
                else if (recipe.requiredHeaders.Any(string.IsNullOrWhiteSpace))
                    errors.Add($"Type lowering '{recipe.name}' RequiredHeaders cannot contain an empty value.");
                ValidateExpression(recipe.parameterToCppExpression, "{value}", $"Type lowering '{recipe.name}' ParameterToCppExpression", errors);
                ValidateExpression(recipe.returnToCExpression, "{value}", $"Type lowering '{recipe.name}' ReturnToCExpression", errors);
                if (recipe.requiresCleanup && string.IsNullOrWhiteSpace(recipe.cleanupFunction))
                    errors.Add($"Type lowering '{recipe.name}' requires CleanupFunction when RequiresCleanup is true.");
                if (recipe.managedProjection != null)
                {
                    if (string.IsNullOrWhiteSpace(recipe.managedProjection.managedType))
                        errors.Add($"Type lowering '{recipe.name}' managed projection requires ManagedType.");
                    if (string.IsNullOrWhiteSpace(recipe.managedProjection.managedToNativeExpression) && string.IsNullOrWhiteSpace(recipe.managedProjection.nativeToManagedExpression))
                        errors.Add($"Type lowering '{recipe.name}' managed projection requires at least one conversion expression.");
                    if (recipe.managedProjection.managedToNativeExpression is { } toNative && !toNative.Contains("{value}", StringComparison.Ordinal))
                        errors.Add($"Type lowering '{recipe.name}' ManagedToNativeExpression must contain '{{value}}'.");
                    if (recipe.managedProjection.nativeToManagedExpression is { } fromNative && !fromNative.Contains("{value}", StringComparison.Ordinal))
                        errors.Add($"Type lowering '{recipe.name}' NativeToManagedExpression must contain '{{value}}'.");
                }
            }

            AddDuplicateNames(config.typeLowerings.Where(value => value != null).Select(value => value.name), "TypeLowerings", errors);
        }

        if (config.callableLowerings == null)
            errors.Add("CallableLowerings cannot be null.");
        else
        {
            foreach (var recipe in config.callableLowerings)
            {
                if (recipe == null)
                {
                    errors.Add("CallableLowerings cannot contain null entries.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(recipe.name))
                    errors.Add("CallableLowerings entries require a non-empty Name.");
                if (string.IsNullOrWhiteSpace(recipe.functionPattern))
                    errors.Add($"Callable lowering '{recipe.name}' requires FunctionPattern.");
                if (!Enum.IsDefined(recipe.safety))
                    errors.Add($"Callable lowering '{recipe.name}' has an invalid Safety value.");
                if (recipe.requiredHeaders == null)
                    errors.Add($"Callable lowering '{recipe.name}' RequiredHeaders cannot be null.");
                else if (recipe.requiredHeaders.Any(string.IsNullOrWhiteSpace))
                    errors.Add($"Callable lowering '{recipe.name}' RequiredHeaders cannot contain an empty value.");
                ValidateExpression(recipe.invocationExpression, "{invocation}", $"Callable lowering '{recipe.name}' InvocationExpression", errors);
            }

            AddDuplicateNames(config.callableLowerings.Where(value => value != null).Select(value => value.name), "CallableLowerings", errors);
        }

        if (config.nativeShims == null)
            errors.Add("NativeShims cannot be null.");
        else
        {
            foreach (var shim in config.nativeShims)
            {
                if (shim == null)
                {
                    errors.Add("NativeShims cannot contain null entries.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(shim.name))
                    errors.Add("NativeShims entries require a non-empty Name.");
                if (!Enum.IsDefined(shim.safety))
                    errors.Add($"Native shim '{shim.name}' has an invalid Safety value.");
                if (shim.publicHeaders == null)
                    errors.Add($"Native shim '{shim.name}' PublicHeaders cannot be null.");
                else if (shim.publicHeaders.Count == 0)
                    errors.Add($"Native shim '{shim.name}' requires at least one PublicHeaders item.");
                if (shim.sourceFiles == null)
                    errors.Add($"Native shim '{shim.name}' SourceFiles cannot be null.");
                if (shim.publicHeaders != null && shim.sourceFiles != null && shim.publicHeaders.Concat(shim.sourceFiles).Any(string.IsNullOrWhiteSpace))
                    errors.Add($"Native shim '{shim.name}' cannot contain an empty file path.");
            }

            AddDuplicateNames(config.nativeShims.Where(value => value != null).Select(value => value.name), "NativeShims", errors);
        }
    }

    private static void ValidateExpression(
        string? expression,
        string placeholder,
        string description,
        List<string> errors
    ) {
        if (expression != null && !expression.Contains(placeholder, StringComparison.Ordinal))
            errors.Add($"{description} must contain '{placeholder}'.");
    }

    private static void AddDuplicateNames(
        IEnumerable<string> names,
        string collection,
        List<string> errors
    ) {
        string? duplicate = names.Where(name => !string.IsNullOrWhiteSpace(name)).GroupBy(name => name, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicate != null)
            errors.Add($"{collection} contains duplicate name '{duplicate}'.");
    }
}
