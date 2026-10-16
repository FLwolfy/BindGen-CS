using System;
using System.Threading;
using System.Threading.Tasks;

namespace BGCS.Tool.Commands;

internal static class GenerationArguments
{
    private const string C_DEFAULT_CONFIG = "bindgen.json";
    internal sealed record Options(
        string configPath,
        string? outputPath,
        string? profile,
        string? target,
        bool json
    )
    {
        internal DiagnosticPresentation.Options diagnostics { get; init; } = new();
        internal bool configurationExplicit { get; init; }
    }

    internal static Options ParseOptions(string[] args)
    {
        args = DiagnosticPresentation.Extract(args, out DiagnosticPresentation.Options diagnostics);
        string config = C_DEFAULT_CONFIG;
        string? output = null;
        string? profile = null;
        string? target = null;
        bool json = false;
        bool hasConfig = false;
        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--config" or "-c":
                    if (hasConfig) throw new ArgumentException("The configuration was supplied more than once.");
                    config = ReadValue(args, ref index, args[index]);
                    hasConfig = true;
                    break;
                case "--output" or "-o":
                    output = ReadValue(args, ref index, args[index]);
                    break;
                case "--profile":
                    profile = ReadValue(args, ref index, args[index]);
                    break;
                case "--target":
                    target = ReadValue(args, ref index, args[index]);
                    break;
                case "--json": json = true; break;
                default:
                    if (args[index].StartsWith('-') || hasConfig) throw new ArgumentException($"Unknown command argument '{args[index]}'.");
                    config = args[index];
                    hasConfig = true;
                    break;
            }
        }
        return new(config, output, profile, target, json) { diagnostics = diagnostics, configurationExplicit = hasConfig };
    }

    internal static async Task<GenerationRequest> CreateRequestAsync(
        Options options,
        CancellationToken cancellationToken
    ) {
        GenerationRequest request = await GenerationRequest.FromFileAsync(options.configPath, options.profile, cancellationToken).ConfigureAwait(false);
        BGCS.Configuration.BindingConfiguration configuration = request.configuration;
        if (options.outputPath is not null)
            configuration = configuration with { output = configuration.output with { root = System.IO.Path.GetFullPath(options.outputPath) } };
        if (options.target is not null)
        {
            BGCS.Configuration.TargetConfiguration selected = System.Linq.Enumerable.Single(configuration.targets, target => target.id == options.target);
            configuration = configuration with { targets = [selected] };
        }
        return request.WithConfiguration(configuration);
    }
    private static string ReadValue(
        string[] args,
        ref int index,
        string option
    ) {
        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
        {
            throw new ArgumentException($"Option '{option}' requires a value.");
        }

        return args[index];
    }
}
