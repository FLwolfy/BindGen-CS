using System.Text.Json;
using BGCS.Intermediate;

namespace BGCS.Tool.Commands;

internal static class ExplainCommand
{
    internal static int Run(string[] args, TextWriter output, TextWriter error)
    {
        bool json = args.Contains("--json", StringComparer.Ordinal);
        string[] positional = args.Where(argument => argument != "--json").ToArray();
        if (positional.Length > 1 || args.Any(argument => argument.StartsWith("-", StringComparison.Ordinal) && argument != "--json"))
            return Fail(error, "explain accepts zero or one diagnostic code and the optional --json flag.");

        if (positional.Length == 0)
        {
            if (json)
                output.WriteLine(JsonSerializer.Serialize(BindingDiagnosticCatalog.All, JsonOptions));
            else
                foreach (BindingDiagnosticDescriptor descriptor in BindingDiagnosticCatalog.All)
                    output.WriteLine($"{descriptor.Code}: {descriptor.Title}");
            return 0;
        }

        string code = positional[0];
        if (!BindingDiagnosticCatalog.TryGet(code, out BindingDiagnosticDescriptor descriptorForCode))
            return Fail(error, $"Unknown diagnostic code '{code}'. Run 'bindgen-cs explain' to list known codes.");

        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(descriptorForCode, JsonOptions));
        }
        else
        {
            output.WriteLine($"{descriptorForCode.Code}: {descriptorForCode.Title}");
            output.WriteLine($"Cause: {descriptorForCode.Cause}");
            output.WriteLine($"Resolution: {descriptorForCode.Resolution}");
        }
        return 0;
    }

    private static JsonSerializerOptions JsonOptions { get; } = new() { WriteIndented = true };

    private static int Fail(TextWriter error, string message)
    {
        error.WriteLine($"error: {message}");
        error.WriteLine("Run 'bindgen-cs --help' for usage.");
        return 2;
    }
}
