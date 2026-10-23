using System;
using System.Text.Json;

namespace BGCS.Tool.Commands;

internal static class BridgeCommand
{
    internal static int Run(string[] args)
    {
        GenerationArguments.Options options = GenerationArguments.ParseOptions(args);
        using CommandCancellation cancellation = new();
        return ExecuteAsync().GetAwaiter().GetResult();
        async System.Threading.Tasks.Task<int> ExecuteAsync()
        {
            GenerationRequest request = await GenerationArguments.CreateRequestAsync(options, cancellation.token).ConfigureAwait(false);
            await using BindingHost host = BindingGenerator.CreateDefault();
            GenerationResult result = await host.BridgeAsync(request, cancellation.token).ConfigureAwait(false);
            cancellation.token.ThrowIfCancellationRequested();
            GenerateCommand.WriteDiagnostics(result.diagnostics, options.diagnostics);
            if (options.json) Console.WriteLine(JsonSerializer.Serialize(result, GenerateCommand.JsonOptions));
            else if (result.success) Console.WriteLine($"Generated {result.artifacts.Length} bridge artifacts in {result.outputRoot}");
            return result.success ? 0 : 1;
        }
    }
}
