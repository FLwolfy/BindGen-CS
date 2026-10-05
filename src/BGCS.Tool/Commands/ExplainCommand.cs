using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using BGCS.Intermediate;

namespace BGCS.Tool.Commands;

internal static class ExplainCommand
{
    internal static int Run(
        string[] args,
        TextWriter output,
        TextWriter error
    ) {
        bool json = args.Contains("--json", StringComparer.Ordinal);
        string[] positional = args.Where(argument => argument != "--json").ToArray();
        if (positional.Length > 1 || args.Any(argument => argument.StartsWith("-", StringComparison.Ordinal) && argument != "--json"))
            return Fail(error, "explain accepts zero or one diagnostic code and the optional --json flag.");
        if (positional.Length == 0)
        {
            if (json)
                output.WriteLine(JsonSerializer.Serialize(BindingDiagnosticCatalog.all, global::BGCS.Tool.Commands.ExplainCommand.jsonOptions));
            else
                foreach (BindingDiagnosticDescriptor descriptor in BindingDiagnosticCatalog.all)
                    output.WriteLine($"{descriptor.code}: {descriptor.title}");
            return 0;
        }

        string code = positional[0];
        if (!BindingDiagnosticCatalog.TryGet(code, out BindingDiagnosticDescriptor descriptorForCode))
            return Fail(error, $"Unknown diagnostic code '{code}'. Run 'bindgen-cs explain' to list known codes.");
        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(descriptorForCode, global::BGCS.Tool.Commands.ExplainCommand.jsonOptions));
        }
        else
        {
            output.WriteLine($"{descriptorForCode.code}: {descriptorForCode.title}");
            output.WriteLine($"Cause: {descriptorForCode.cause}");
            output.WriteLine($"Resolution: {descriptorForCode.resolution}");
        }

        return 0;
    }

    private static JsonSerializerOptions jsonOptions { get; } = new()
    {
        WriteIndented = true
    };

    private static int Fail(
        TextWriter error,
        string message
    ) {
        error.WriteLine($"error: {message}");
        error.WriteLine("Run 'bindgen-cs --help' for usage.");
        return 2;
    }
}
