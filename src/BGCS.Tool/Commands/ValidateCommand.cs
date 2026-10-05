using System;
using System.Linq;
using System.Text.Json;
using BGCS.Facade;
using BGCS.Intermediate;
using BGCS.Tool.Output;

namespace BGCS.Tool.Commands;

internal static class ValidateCommand
{
    internal static int Run(string[] args)
    {
        string configPath = GenerationArguments.ParseConfiguration(args);
        CsCodeGenerator generator = CsCodeGenerator.Create(configPath);
        using var logging = GenerationDiagnosticWriter.Attach(generator, Console.Out);
        BindingGenerationResult<BindingModule> result = generator.AnalyzeConfigured();
        if (!result.success)
            return 1;
        Console.WriteLine($"Validated {result.module!.types.Count} types and {result.module.functions.Count} functions.");
        if (result.module.diagnostics.Count > 0)
        {
            Console.WriteLine($"IR diagnostics: {result.module.diagnostics.Count}");
            return 1;
        }

        return 0;
    }

    internal static int Inspect(string[] args)
    {
        bool json = args.Contains("--json", StringComparer.Ordinal);
        string configPath = GenerationArguments.ParseConfiguration(args.Where(argument => argument != "--json").ToArray());
        CsCodeGenerator generator = CsCodeGenerator.Create(configPath);
        BindingGenerationResult<BindingModule> result = generator.AnalyzeConfigured();
        if (!result.success || result.module == null)
            return 1;
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(result.module, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            Console.WriteLine($"Module: {result.module.name}");
            Console.WriteLine($"Target ABI: {result.module.targetAbi}");
            Console.WriteLine($"Types: {result.module.types.Count}");
            Console.WriteLine($"Functions: {result.module.functions.Count}");
            Console.WriteLine($"IR diagnostics: {result.module.diagnostics.Count}");
        }

        return result.module.diagnostics.Count == 0 ? 0 : 1;
    }
}
