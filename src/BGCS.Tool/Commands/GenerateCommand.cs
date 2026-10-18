using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using BGCS.Contracts;

namespace BGCS.Tool.Commands;

internal static class GenerateCommand
{
    internal static int Run(string[] args) => Execute(args);

    internal static int Diff(string[] args)
    {
        GenerationArguments.Options options = GenerationArguments.ParseOptions(args);
        using CommandCancellation cancellation = new();
        return ExecuteAsync().GetAwaiter().GetResult();
        async System.Threading.Tasks.Task<int> ExecuteAsync()
        {
            GenerationRequest request = await GenerationArguments.CreateRequestAsync(options, cancellation.token).ConfigureAwait(false);
            await using BindingHost host = BindingHost.CreateDefault();
            GenerationDiffResult result = await host.DiffAsync(request, cancellation.token).ConfigureAwait(false);
            cancellation.token.ThrowIfCancellationRequested();
            WriteDiagnostics(result.diagnostics, options.diagnostics);
            if (options.json) Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            else foreach (ArtifactChange change in result.changes) Console.WriteLine($"{change.kind}: {change.relativePath}");
            if (!result.success) return 2;
            return result.hasChanges ? 1 : 0;
        }
    }

    private static int Execute(
        string[] args
    )
    {
        GenerationArguments.Options options = GenerationArguments.ParseOptions(args);
        using CommandCancellation cancellation = new();
        return ExecuteAsync().GetAwaiter().GetResult();

        async System.Threading.Tasks.Task<int> ExecuteAsync()
        {
            GenerationRequest request = await GenerationArguments.CreateRequestAsync(options, cancellation.token).ConfigureAwait(false);
            await using BindingHost host = BindingHost.CreateDefault();
            GenerationResult result = await host.GenerateAsync(request, cancellation.token).ConfigureAwait(false);
            cancellation.token.ThrowIfCancellationRequested();
            WriteDiagnostics(result.diagnostics, options.diagnostics);
            if (options.json) Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            else if (result.success) Console.WriteLine($"Generated {result.artifacts.Length} artifacts in {result.outputRoot}");
            return result.success ? 0 : 1;
        }
    }

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    internal static void WriteDiagnostics(
        System.Collections.Generic.IEnumerable<BindingDiagnostic> diagnostics,
        DiagnosticPresentation.Options? options = null
    ) => DiagnosticPresentation.Write(diagnostics, Console.Error, options);
}
