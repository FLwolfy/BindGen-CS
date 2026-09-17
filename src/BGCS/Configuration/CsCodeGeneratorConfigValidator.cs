namespace BGCS.Configuration;

using BGCS.Output;
using Microsoft.CodeAnalysis.CSharp;

internal static class CsCodeGeneratorConfigValidator
{
    internal static void Validate(CsCodeGeneratorConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        List<string> errors = [];
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
