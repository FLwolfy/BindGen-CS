using System;
using System.Text.Json;

namespace BGCS.Tool.Commands;

internal static class ValidateCommand
{
    internal static int Run(string[] args) => Execute(args, inspect: false);
    internal static int Inspect(string[] args) => Execute(args, inspect: true);

    private static int Execute(
        string[] args,
        bool inspect
    ) {
        GenerationArguments.Options options = GenerationArguments.ParseOptions(args);
        if (options.outputPath is not null) throw new ArgumentException("Inspection and validation do not accept --output.");
        using CommandCancellation cancellation = new();
        return ExecuteAsync().GetAwaiter().GetResult();
        async System.Threading.Tasks.Task<int> ExecuteAsync()
        {
            GenerationRequest request = await GenerationArguments.CreateRequestAsync(options, cancellation.token).ConfigureAwait(false);
            await using BindingHost host = BindingGenerator.CreateDefault();
            InspectionResult result = await host.InspectAsync(request, cancellation.token).ConfigureAwait(false);
            cancellation.token.ThrowIfCancellationRequested();
            GenerateCommand.WriteDiagnostics(result.diagnostics, options.diagnostics);
            if (options.json) Console.WriteLine(JsonSerializer.Serialize(result, GenerateCommand.JsonOptions));
            else foreach (TargetInspection target in result.targets)
                Console.WriteLine($"{(inspect ? "Inspected" : "Validated")} {target.targetId}: {target.native.types.Length} types, {target.interop.functions.Length} ABI functions");
            return result.success ? 0 : 1;
        }
    }
}
