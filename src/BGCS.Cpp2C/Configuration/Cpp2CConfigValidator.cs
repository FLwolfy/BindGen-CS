namespace BGCS.Cpp2C.Configuration;

using System.Text.RegularExpressions;

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
        if (!Enum.IsDefined(config.CSharpStrictSafetySeverity))
            errors.Add("CSharpStrictSafetySeverity is invalid.");
        if (config.EnableIncrementalCache && string.IsNullOrWhiteSpace(config.CacheDirectory))
            errors.Add("CacheDirectory is required when incremental caching is enabled.");
        if (config.PluginAssemblies == null)
            errors.Add("PluginAssemblies cannot be null.");
        else if (config.PluginAssemblies.Any(string.IsNullOrWhiteSpace))
            errors.Add("PluginAssemblies cannot contain an empty path.");
        ValidateLoweringRecipes(config, errors);
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

    private static void ValidateLoweringRecipes(Cpp2CGeneratorConfig config, List<string> errors)
    {
        if (config.TypeLowerings == null)
            errors.Add("TypeLowerings cannot be null.");
        else
        {
            foreach (var recipe in config.TypeLowerings)
            {
                if (recipe == null)
                {
                    errors.Add("TypeLowerings cannot contain null entries.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(recipe.Name)) errors.Add("TypeLowerings entries require a non-empty Name.");
                if (string.IsNullOrWhiteSpace(recipe.TypePattern)) errors.Add($"Type lowering '{recipe.Name}' requires TypePattern.");
                if (string.IsNullOrWhiteSpace(recipe.CAbiType)) errors.Add($"Type lowering '{recipe.Name}' requires CAbiType.");
                if (!Enum.IsDefined(recipe.Safety)) errors.Add($"Type lowering '{recipe.Name}' has an invalid Safety value.");
                if (!Enum.IsDefined(recipe.AbiShape)) errors.Add($"Type lowering '{recipe.Name}' has an invalid AbiShape value.");
                if (recipe.AbiParameters == null)
                    errors.Add($"Type lowering '{recipe.Name}' AbiParameters cannot be null.");
                else
                {
                    HashSet<string> suffixes = new(StringComparer.Ordinal);
                    foreach (var parameter in recipe.AbiParameters)
                    {
                        if (parameter == null ||
                            !Regex.IsMatch(parameter.NameSuffix, "^[_A-Za-z0-9]*$", RegexOptions.CultureInvariant) ||
                            string.IsNullOrWhiteSpace(parameter.CAbiType) ||
                            !suffixes.Add(parameter.NameSuffix))
                        {
                            errors.Add($"Type lowering '{recipe.Name}' contains invalid or duplicate AbiParameters metadata.");
                            break;
                        }
                    }
                }
                if (recipe.RequiredHeaders == null)
                    errors.Add($"Type lowering '{recipe.Name}' RequiredHeaders cannot be null.");
                else if (recipe.RequiredHeaders.Any(string.IsNullOrWhiteSpace))
                    errors.Add($"Type lowering '{recipe.Name}' RequiredHeaders cannot contain an empty value.");
                ValidateExpression(recipe.ParameterToCppExpression, "{value}", $"Type lowering '{recipe.Name}' ParameterToCppExpression", errors);
                ValidateExpression(recipe.ReturnToCExpression, "{value}", $"Type lowering '{recipe.Name}' ReturnToCExpression", errors);
                if (recipe.RequiresCleanup && string.IsNullOrWhiteSpace(recipe.CleanupFunction))
                    errors.Add($"Type lowering '{recipe.Name}' requires CleanupFunction when RequiresCleanup is true.");
                if (recipe.ManagedProjection != null)
                {
                    if (string.IsNullOrWhiteSpace(recipe.ManagedProjection.ManagedType))
                        errors.Add($"Type lowering '{recipe.Name}' managed projection requires ManagedType.");
                    if (string.IsNullOrWhiteSpace(recipe.ManagedProjection.ManagedToNativeExpression) &&
                        string.IsNullOrWhiteSpace(recipe.ManagedProjection.NativeToManagedExpression))
                        errors.Add($"Type lowering '{recipe.Name}' managed projection requires at least one conversion expression.");
                    if (recipe.ManagedProjection.ManagedToNativeExpression is { } toNative && !toNative.Contains("{value}", StringComparison.Ordinal))
                        errors.Add($"Type lowering '{recipe.Name}' ManagedToNativeExpression must contain '{{value}}'.");
                    if (recipe.ManagedProjection.NativeToManagedExpression is { } fromNative && !fromNative.Contains("{value}", StringComparison.Ordinal))
                        errors.Add($"Type lowering '{recipe.Name}' NativeToManagedExpression must contain '{{value}}'.");
                }
            }
            AddDuplicateNames(config.TypeLowerings.Where(value => value != null).Select(value => value.Name), "TypeLowerings", errors);
        }
        if (config.CallableLowerings == null)
            errors.Add("CallableLowerings cannot be null.");
        else
        {
            foreach (var recipe in config.CallableLowerings)
            {
                if (recipe == null)
                {
                    errors.Add("CallableLowerings cannot contain null entries.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(recipe.Name)) errors.Add("CallableLowerings entries require a non-empty Name.");
                if (string.IsNullOrWhiteSpace(recipe.FunctionPattern)) errors.Add($"Callable lowering '{recipe.Name}' requires FunctionPattern.");
                if (!Enum.IsDefined(recipe.Safety)) errors.Add($"Callable lowering '{recipe.Name}' has an invalid Safety value.");
                if (recipe.RequiredHeaders == null)
                    errors.Add($"Callable lowering '{recipe.Name}' RequiredHeaders cannot be null.");
                else if (recipe.RequiredHeaders.Any(string.IsNullOrWhiteSpace))
                    errors.Add($"Callable lowering '{recipe.Name}' RequiredHeaders cannot contain an empty value.");
                ValidateExpression(recipe.InvocationExpression, "{invocation}", $"Callable lowering '{recipe.Name}' InvocationExpression", errors);
            }
            AddDuplicateNames(config.CallableLowerings.Where(value => value != null).Select(value => value.Name), "CallableLowerings", errors);
        }
        if (config.NativeShims == null)
            errors.Add("NativeShims cannot be null.");
        else
        {
            foreach (var shim in config.NativeShims)
            {
                if (shim == null)
                {
                    errors.Add("NativeShims cannot contain null entries.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(shim.Name)) errors.Add("NativeShims entries require a non-empty Name.");
                if (!Enum.IsDefined(shim.Safety)) errors.Add($"Native shim '{shim.Name}' has an invalid Safety value.");
                if (shim.PublicHeaders == null)
                    errors.Add($"Native shim '{shim.Name}' PublicHeaders cannot be null.");
                else if (shim.PublicHeaders.Count == 0)
                    errors.Add($"Native shim '{shim.Name}' requires at least one PublicHeaders item.");
                if (shim.SourceFiles == null)
                    errors.Add($"Native shim '{shim.Name}' SourceFiles cannot be null.");
                if (shim.PublicHeaders != null && shim.SourceFiles != null &&
                    shim.PublicHeaders.Concat(shim.SourceFiles).Any(string.IsNullOrWhiteSpace))
                    errors.Add($"Native shim '{shim.Name}' cannot contain an empty file path.");
            }
            AddDuplicateNames(config.NativeShims.Where(value => value != null).Select(value => value.Name), "NativeShims", errors);
        }
    }

    private static void ValidateExpression(string? expression, string placeholder, string description, List<string> errors)
    {
        if (expression != null && !expression.Contains(placeholder, StringComparison.Ordinal))
            errors.Add($"{description} must contain '{placeholder}'.");
    }

    private static void AddDuplicateNames(IEnumerable<string> names, string collection, List<string> errors)
    {
        string? duplicate = names.Where(name => !string.IsNullOrWhiteSpace(name))
            .GroupBy(name => name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicate != null)
            errors.Add($"{collection} contains duplicate name '{duplicate}'.");
    }
}
