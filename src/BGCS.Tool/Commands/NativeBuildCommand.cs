using System.Text.Json;
using System.ComponentModel;
using BGCS.Cpp2C.Build;
using BGCS.CppAst.Parsing;
using BGCS.CppAst.Targeting;

namespace BGCS.Tool.Commands;

internal static class NativeBuildCommand
{
    private const string DefaultManifestPath = "GeneratedBridge/bridge.manifest.json";

    internal static int Run(string[] args, string workingDirectory, TextWriter output, TextWriter error)
    {
        try
        {
            Options options = Parse(args);
            string manifestPath = Path.GetFullPath(options.ManifestPath ?? DefaultManifestPath, workingDirectory);
            CppBridgeBuildManifest manifest = CppBridgeBuildManifestSerializer.Load(manifestPath);
            INativeBuildPipelineProvider provider = CreateProvider(options, manifest, manifestPath);
            string? outputPath = options.OutputPath == null
                ? null
                : Path.GetFullPath(options.OutputPath, workingDirectory);
            NativeBuildPipeline pipeline = provider.CreatePipeline(manifest, manifestPath, outputPath);
            if (options.DryRun)
            {
                if (options.Json)
                    output.WriteLine(JsonSerializer.Serialize(pipeline, JsonOptions));
                else
                    PrintPlan(pipeline, output);
                return 0;
            }

            NativeBuildPipelineResult result = NativeBuildExecutor.Execute(pipeline, TimeSpan.FromSeconds(options.TimeoutSeconds));
            NativeExportInspectionResult? exports = null;
            if (result.Success && options.VerifyExports)
                exports = NativeExportInspector.Inspect(manifest, manifestPath, result.OutputFile, options.ExportToolPath);
            NativeAssetLayoutResult? packagedAsset = null;
            if (result.Success && exports?.Success != false && options.PackageRoot != null)
                packagedAsset = NativeAssetLayout.Stage(manifest, result.OutputFile,
                    Path.GetFullPath(options.PackageRoot, workingDirectory));
            NativeBuildCommandResult commandResult = new(
                result.Success && exports?.Success != false,
                result.Provider,
                result.Steps,
                result.TimedOut,
                result.OutputFile,
                exports,
                packagedAsset);
            if (options.Json)
            {
                output.WriteLine(JsonSerializer.Serialize(commandResult, JsonOptions));
            }
            else
            {
                foreach (NativeBuildStepResult step in result.Steps)
                {
                    if (!string.IsNullOrWhiteSpace(step.StandardOutput))
                        output.Write(step.StandardOutput);
                    if (!string.IsNullOrWhiteSpace(step.StandardError))
                        error.Write(step.StandardError);
                }
            }
            if (!result.Success)
            {
                int exitCode = result.Steps.LastOrDefault()?.ExitCode ?? -1;
                string reason = result.TimedOut ? $"timed out after {options.TimeoutSeconds} seconds" : $"exited with code {exitCode}";
                error.WriteLine($"error: Native bridge build {reason}. Expected output: {result.OutputFile}");
                return 1;
            }
            if (exports?.Success == false)
            {
                error.WriteLine($"error: Native bridge is missing {exports.Missing.Count} declared export(s): {string.Join(", ", exports.Missing)}");
                return 1;
            }
            if (!options.Json)
            {
                output.WriteLine($"Native bridge build succeeded: {result.OutputFile}");
                if (exports != null)
                    output.WriteLine($"Verified {exports.Expected.Count} native exports with {exports.Tool}.");
                if (packagedAsset != null)
                    output.WriteLine($"Staged {packagedAsset.RuntimeIdentifier} asset: {packagedAsset.AssetPath}");
            }
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or InvalidDataException or NotSupportedException or JsonException or Win32Exception or TimeoutException)
        {
            return Fail(error, exception.Message);
        }
    }

    private static INativeBuildPipelineProvider CreateProvider(Options options, CppBridgeBuildManifest manifest, string manifestPath)
    {
        string provider = options.Provider.ToLowerInvariant();
        string? configuredCompiler = ResolveConfiguredTool(options.CompilerPath ?? manifest.CompilerPath, manifestPath);
        if (provider == "auto")
        {
            if (OperatingSystem.IsWindows() && manifest.TargetIdentifier.StartsWith("windows-", StringComparison.OrdinalIgnoreCase))
            {
                if (options.BuildToolPath != null)
                    provider = "msbuild";
                else if (configuredCompiler != null && !LooksLikeClangCl(configuredCompiler))
                {
                    if (!LooksLikeGnuDriver(configuredCompiler))
                        throw new InvalidOperationException(
                            $"Cannot infer the command-line interface of compiler '{configuredCompiler}'. Select --provider explicitly.");
                    provider = "clang";
                }
                else
                    provider = NativeBuildToolDiscovery.FindClangCl(configuredCompiler) == null ? "msbuild" : "clang-cl";
            }
            else
            {
                provider = "clang";
            }
        }
        string? explicitClangCl = LooksLikeClangCl(configuredCompiler) ? configuredCompiler : null;
        return provider switch
        {
            "clang" => new ClangNativeBuildProvider(FindCompiler(configuredCompiler)),
            "clang-cl" => new ClangClNativeBuildProvider(
                explicitClangCl ?? NativeBuildToolDiscovery.FindClangCl()
                ?? throw new InvalidOperationException("clang-cl was not found. Install LLVM for Windows or pass --compiler <clang-cl.exe>.")),
            "cmake" => new CMakeNativeBuildProvider(options.BuildToolPath ?? "cmake", configuredCompiler),
            "meson" => new MesonNativeBuildProvider(options.BuildToolPath ?? "meson", configuredCompiler),
            "msbuild" => new MSBuildNativeBuildProvider(
                options.BuildToolPath ?? NativeBuildToolDiscovery.FindMSBuild()
                ?? throw new InvalidOperationException("MSBuild was not found. Install Visual Studio C++ build tools or pass --build-tool <MSBuild.exe>.")),
            _ => throw new ArgumentException($"Unknown native build provider '{options.Provider}'. Expected auto, clang, clang-cl, cmake, meson, or msbuild.")
        };
    }

    private static bool LooksLikeClangCl(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Path.GetFileNameWithoutExtension(path).Equals("clang-cl", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeGnuDriver(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        return name.EndsWith("clang++", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("g++", StringComparison.OrdinalIgnoreCase) ||
            name is "c++" or "clang" or "gcc" or "cc";
    }

    private static string FindCompiler(string? configuredCompiler) =>
        CppToolchainDiscovery.FindCompiler(CppParserKind.Cpp, configuredCompiler)
        ?? throw new InvalidOperationException("No C++ compiler driver was found. Use --compiler, BGCS_CPP2C_CXX, or CXX.");

    private static string? ResolveConfiguredTool(string? tool, string manifestPath)
    {
        if (!string.IsNullOrWhiteSpace(tool) && !Path.IsPathRooted(tool) && (tool.Contains('/') || tool.Contains('\\')))
            return Path.GetFullPath(tool, Path.GetDirectoryName(manifestPath)!);
        return tool;
    }

    private static void PrintPlan(NativeBuildPipeline plan, TextWriter output)
    {
        output.WriteLine($"Provider: {plan.Provider}");
        output.WriteLine($"Output: {plan.OutputFile}");
        foreach (NativeBuildInputFile input in plan.InputFiles)
            output.WriteLine($"Generated input: {input.Path}");
        foreach (NativeBuildStep step in plan.Steps)
        {
            output.WriteLine($"Step: {step.Name}");
            output.WriteLine($"  Executable: {step.Executable}");
            output.WriteLine($"  Working directory: {step.WorkingDirectory}");
            output.WriteLine("  Arguments:");
            foreach (string argument in step.Arguments)
                output.WriteLine($"    {argument}");
        }
    }

    private static Options Parse(string[] args)
    {
        string? manifestPath = null;
        string? outputPath = null;
        string? compilerPath = null;
        string? buildToolPath = null;
        string? exportToolPath = null;
        string? packageRoot = null;
        string provider = "auto";
        int timeoutSeconds = 300;
        bool dryRun = false;
        bool json = false;
        bool verifyExports = true;
        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            if (argument is "--output" or "-o")
                outputPath = ReadValue(args, ref index, argument);
            else if (argument == "--compiler")
                compilerPath = ReadValue(args, ref index, argument);
            else if (argument == "--provider")
                provider = ReadValue(args, ref index, argument);
            else if (argument == "--build-tool")
                buildToolPath = ReadValue(args, ref index, argument);
            else if (argument == "--export-tool")
                exportToolPath = ReadValue(args, ref index, argument);
            else if (argument == "--package-root")
                packageRoot = ReadValue(args, ref index, argument);
            else if (argument == "--timeout")
            {
                string value = ReadValue(args, ref index, argument);
                if (!int.TryParse(value, out timeoutSeconds) || timeoutSeconds <= 0)
                    throw new ArgumentException("--timeout must be a positive number of seconds.");
            }
            else if (argument == "--dry-run")
                dryRun = true;
            else if (argument == "--json")
                json = true;
            else if (argument == "--no-verify-exports")
                verifyExports = false;
            else if (argument.StartsWith("-", StringComparison.Ordinal))
                throw new ArgumentException($"Unknown native-build option '{argument}'.");
            else if (manifestPath == null)
                manifestPath = argument;
            else
                throw new ArgumentException("native-build accepts at most one manifest path.");
        }
        return new(manifestPath, outputPath, compilerPath, buildToolPath, exportToolPath, packageRoot, provider, timeoutSeconds, dryRun, json, verifyExports);
    }

    private static string ReadValue(string[] args, ref int index, string option)
    {
        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
            throw new ArgumentException($"Option '{option}' requires a value.");
        return args[index];
    }

    private static int Fail(TextWriter error, string message)
    {
        error.WriteLine($"error: {message}");
        error.WriteLine("Run 'bindgen-cs --help' for usage.");
        return 2;
    }

    private static JsonSerializerOptions JsonOptions { get; } = new() { WriteIndented = true };

    private sealed record Options(
        string? ManifestPath,
        string? OutputPath,
        string? CompilerPath,
        string? BuildToolPath,
        string? ExportToolPath,
        string? PackageRoot,
        string Provider,
        int TimeoutSeconds,
        bool DryRun,
        bool Json,
        bool VerifyExports);

    private sealed record NativeBuildCommandResult(
        bool Success,
        string Provider,
        IReadOnlyList<NativeBuildStepResult> Steps,
        bool TimedOut,
        string OutputFile,
        NativeExportInspectionResult? Exports,
        NativeAssetLayoutResult? PackagedAsset);
}
