using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BGCS.Tool.Commands;

internal static class NativeBuildCommand
{
    internal static int Run(
        string[] args,
        string workingDirectory,
        TextWriter output,
        TextWriter error
    ) {
        args = DiagnosticPresentation.Extract(args, out DiagnosticPresentation.Options diagnostics);
        Options options = Parse(args);
        using CommandCancellation cancellation = new();
        string invocation = Path.GetFullPath(workingDirectory);
        string manifest = options.manifestPath is not null ? Path.GetFullPath(options.manifestPath, invocation) : FindDefaultManifest(invocation);
        ManifestNativeBuildRequest request = new(manifest)
        {
            providerId = options.provider,
            compilerPath = ResolveTool(options.compilerPath),
            buildToolPath = ResolveTool(options.buildToolPath),
            auxiliaryToolPath = ResolveTool(options.auxiliaryToolPath),
            exportToolPath = ResolveTool(options.exportToolPath),
            outputFile = options.outputPath is null ? null : Path.GetFullPath(options.outputPath, invocation),
            packageRoot = options.packageRoot is null ? null : Path.GetFullPath(options.packageRoot, invocation),
            verifyExports = options.verifyExports,
            timeoutSeconds = options.timeoutSeconds
        };
        return ExecuteAsync().GetAwaiter().GetResult();

        string? ResolveTool(string? path) => path is null ? null : Path.IsPathRooted(path) || path.Contains('/') || path.Contains('\\') ? Path.GetFullPath(path, invocation) : path;
        async System.Threading.Tasks.Task<int> ExecuteAsync()
        {
            await using BindingHost host = BindingHost.CreateDefault();
            if (options.dryRun)
            {
                ManifestNativeBuildPlanResult result = await host.PlanNativeBuildAsync(request, cancellation.token).ConfigureAwait(false);
                cancellation.token.ThrowIfCancellationRequested();
                DiagnosticPresentation.Write(result.diagnostics, error, diagnostics);
                if (options.json) output.WriteLine(JsonSerializer.Serialize(result, GenerateCommand.JsonOptions));
                else if (result.plan is not null)
                {
                    output.WriteLine($"Provider: {result.plan.providerId}");
                    output.WriteLine($"Native output: {result.nativeArtifact}");
                    output.WriteLine(JsonSerializer.Serialize(result.plan, GenerateCommand.JsonOptions));
                }
                return result.success ? 0 : 1;
            }
            ManifestNativeBuildResult built = await host.NativeBuildAsync(request, cancellation.token).ConfigureAwait(false);
            cancellation.token.ThrowIfCancellationRequested();
            DiagnosticPresentation.Write(built.diagnostics, error, diagnostics);
            if (options.json) output.WriteLine(JsonSerializer.Serialize(built, GenerateCommand.JsonOptions));
            else if (built.success) output.WriteLine($"Built {built.nativeArtifact}");
            return built.success ? 0 : 1;
        }
    }

    private static string FindDefaultManifest(string invocation)
    {
        string output = Path.Combine(invocation, "Generated");
        string[] matches = Directory.Exists(output) ? Directory.GetFiles(output, "bridge.manifest.json", SearchOption.AllDirectories) : [];
        return matches.Length == 1 ? matches[0] : throw new ArgumentException("native-build requires an explicit bridge.manifest.json when Generated contains zero or multiple target manifests.");
    }

    private static Options Parse(string[] args)
    {
        string? manifestPath = null;
        string? outputPath = null;
        string? compilerPath = null;
        string? buildToolPath = null;
        string? auxiliaryToolPath = null;
        string? exportToolPath = null;
        string? packageRoot = null;
        string provider = "auto";
        int timeoutSeconds = 900;
        bool dryRun = false;
        bool json = false;
        bool verifyExports = true;
        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--provider":
                    provider = ReadValue(args, ref index, args[index]);
                    if (!Regex.IsMatch(provider, "^[A-Za-z0-9][A-Za-z0-9._/-]*$", RegexOptions.CultureInvariant))
                        throw new ArgumentException("A native build provider requires a stable service ID using ASCII letters, digits, dots, slashes, underscores or hyphens.");
                    break;
                case "--output" or "-o": outputPath = ReadValue(args, ref index, args[index]); break;
                case "--compiler": compilerPath = ReadValue(args, ref index, args[index]); break;
                case "--build-tool": buildToolPath = ReadValue(args, ref index, args[index]); break;
                case "--auxiliary-tool": auxiliaryToolPath = ReadValue(args, ref index, args[index]); break;
                case "--export-tool": exportToolPath = ReadValue(args, ref index, args[index]); break;
                case "--package-root": packageRoot = ReadValue(args, ref index, args[index]); break;
                case "--timeout":
                    string timeout = ReadValue(args, ref index, args[index]);
                    if (!int.TryParse(timeout, out timeoutSeconds) || timeoutSeconds <= 0) throw new ArgumentException("--timeout must be a positive number of seconds.");
                    break;
                case "--dry-run": dryRun = true; break;
                case "--json": json = true; break;
                case "--no-verify-exports": verifyExports = false; break;
                default:
                    if (args[index].StartsWith('-') || manifestPath is not null) throw new ArgumentException($"Unknown native-build argument '{args[index]}'.");
                    manifestPath = args[index];
                    break;
            }
        }
        return new(manifestPath, outputPath, compilerPath, buildToolPath, auxiliaryToolPath, exportToolPath, packageRoot, provider, timeoutSeconds, dryRun, json, verifyExports);
    }

    private static string ReadValue(
        string[] args,
        ref int index,
        string option
    ) {
        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index])) throw new ArgumentException($"{option} requires a value.");
        return args[index];
    }

    private sealed record Options(
        string? manifestPath,
        string? outputPath,
        string? compilerPath,
        string? buildToolPath,
        string? auxiliaryToolPath,
        string? exportToolPath,
        string? packageRoot,
        string provider,
        int timeoutSeconds,
        bool dryRun,
        bool json,
        bool verifyExports
    );
}
