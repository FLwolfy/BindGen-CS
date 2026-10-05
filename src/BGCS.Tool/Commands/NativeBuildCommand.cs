using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using BGCS.Cpp2C.Build;
using BGCS.Cpp2C.Build.Providers;
using BGCS.CppAst.Parsing;
using BGCS.CppAst.Targeting;

namespace BGCS.Tool.Commands;

internal static class NativeBuildCommand
{
    private const string C_DEFAULTMANIFESTPATH = "GeneratedBridge/bridge.manifest.json";
    internal static int Run(
        string[] args,
        string workingDirectory,
        TextWriter output,
        TextWriter error
    ) {
        try
        {
            Options options = Parse(args);
            string manifestPath = Path.GetFullPath(options.manifestPath ?? global::BGCS.Tool.Commands.NativeBuildCommand.C_DEFAULTMANIFESTPATH, workingDirectory);
            CppBridgeBuildManifest manifest = CppBridgeBuildManifestSerializer.Load(manifestPath);
            INativeBuildPipelineProvider provider = CreateProvider(options, manifest, manifestPath);
            string? outputPath = options.outputPath == null ? null : Path.GetFullPath(options.outputPath, workingDirectory);
            NativeBuildPipeline pipeline = provider.CreatePipeline(manifest, manifestPath, outputPath);
            if (options.dryRun)
            {
                if (options.json)
                    output.WriteLine(JsonSerializer.Serialize(pipeline, global::BGCS.Tool.Commands.NativeBuildCommand.jsonOptions));
                else
                    PrintPlan(pipeline, output);
                return 0;
            }

            NativeBuildPipelineResult result = NativeBuildExecutor.Execute(pipeline, TimeSpan.FromSeconds(options.timeoutSeconds));
            NativeExportInspectionResult? exports = null;
            if (result.success && options.verifyExports)
                exports = NativeExportInspector.Inspect(manifest, manifestPath, result.outputFile, options.exportToolPath);
            NativeAssetLayoutResult? packagedAsset = null;
            if (result.success && exports?.success != false && options.packageRoot != null)
                packagedAsset = NativeAssetLayout.Stage(manifest, result.outputFile, Path.GetFullPath(options.packageRoot, workingDirectory));
            NativeBuildCommandResult commandResult = new(result.success && exports?.success != false, result.provider, result.steps, result.timedOut, result.outputFile, exports, packagedAsset);
            if (options.json)
            {
                output.WriteLine(JsonSerializer.Serialize(commandResult, global::BGCS.Tool.Commands.NativeBuildCommand.jsonOptions));
            }
            else
            {
                foreach (NativeBuildStepResult step in result.steps)
                {
                    if (!string.IsNullOrWhiteSpace(step.standardOutput))
                        output.Write(step.standardOutput);
                    if (!string.IsNullOrWhiteSpace(step.standardError))
                        error.Write(step.standardError);
                }
            }

            if (!result.success)
            {
                int exitCode = result.steps.LastOrDefault()?.exitCode ?? -1;
                string reason = result.timedOut ? $"timed out after {options.timeoutSeconds} seconds" : $"exited with code {exitCode}";
                error.WriteLine($"error: Native bridge build {reason}. Expected output: {result.outputFile}");
                return 1;
            }

            if (exports?.success == false)
            {
                error.WriteLine($"error: Native bridge is missing {exports.missing.Count} declared export(s): {string.Join(", ", exports.missing)}");
                return 1;
            }

            if (!options.json)
            {
                output.WriteLine($"Native bridge build succeeded: {result.outputFile}");
                if (exports != null)
                    output.WriteLine($"Verified {exports.expected.Count} native exports with {exports.tool}.");
                if (packagedAsset != null)
                    output.WriteLine($"Staged {packagedAsset.runtimeIdentifier} asset: {packagedAsset.assetPath}");
            }

            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or InvalidDataException or NotSupportedException or JsonException or Win32Exception or TimeoutException)
        {
            return Fail(error, exception.Message);
        }
    }

    private static INativeBuildPipelineProvider CreateProvider(
        Options options,
        CppBridgeBuildManifest manifest,
        string manifestPath
    ) {
        string provider = options.provider.ToLowerInvariant();
        string? configuredCompiler = ResolveConfiguredTool(options.compilerPath ?? manifest.compilerPath, manifestPath);
        if (provider == "auto")
        {
            if (OperatingSystem.IsWindows() && manifest.targetIdentifier.StartsWith("windows-", StringComparison.OrdinalIgnoreCase))
            {
                if (options.buildToolPath != null)
                    provider = "msbuild";
                else if (configuredCompiler != null && !LooksLikeClangCl(configuredCompiler))
                {
                    if (!LooksLikeGnuDriver(configuredCompiler))
                        throw new InvalidOperationException($"Cannot infer the command-line interface of compiler '{configuredCompiler}'. Select --provider explicitly.");
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
            "clang-cl" => new ClangClNativeBuildProvider(explicitClangCl ?? NativeBuildToolDiscovery.FindClangCl() ?? throw new InvalidOperationException("clang-cl was not found. Install LLVM for Windows or pass --compiler <clang-cl.exe>.")),
            "cmake" => new CMakeNativeBuildProvider(options.buildToolPath ?? "cmake", configuredCompiler),
            "meson" => new MesonNativeBuildProvider(options.buildToolPath ?? "meson", configuredCompiler),
            "msbuild" => new MSBuildNativeBuildProvider(options.buildToolPath ?? NativeBuildToolDiscovery.FindMSBuild() ?? throw new InvalidOperationException("MSBuild was not found. Install Visual Studio C++ build tools or pass --build-tool <MSBuild.exe>.")),
            _ => throw new ArgumentException($"Unknown native build provider '{options.provider}'. Expected auto, clang, clang-cl, cmake, meson, or msbuild.")
        };
    }

    private static bool LooksLikeClangCl(string? path) => !string.IsNullOrWhiteSpace(path) && Path.GetFileNameWithoutExtension(path).Equals("clang-cl", StringComparison.OrdinalIgnoreCase);
    private static bool LooksLikeGnuDriver(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        return name.EndsWith("clang++", StringComparison.OrdinalIgnoreCase) || name.EndsWith("g++", StringComparison.OrdinalIgnoreCase) || name is "c++" or "clang" or "gcc" or "cc";
    }

    private static string FindCompiler(string? configuredCompiler) => CppToolchainDiscovery.FindCompiler(CppParserKind.Cpp, configuredCompiler) ?? throw new InvalidOperationException("No C++ compiler driver was found. Use --compiler, BGCS_CPP2C_CXX, or CXX.");
    private static string? ResolveConfiguredTool(
        string? tool,
        string manifestPath
    ) {
        if (!string.IsNullOrWhiteSpace(tool) && !Path.IsPathRooted(tool) && (tool.Contains('/') || tool.Contains('\\')))
            return Path.GetFullPath(tool, Path.GetDirectoryName(manifestPath)!);
        return tool;
    }

    private static void PrintPlan(
        NativeBuildPipeline plan,
        TextWriter output
    ) {
        output.WriteLine($"Provider: {plan.provider}");
        output.WriteLine($"Output: {plan.outputFile}");
        foreach (NativeBuildInputFile input in plan.inputFiles)
            output.WriteLine($"Generated input: {input.path}");
        foreach (NativeBuildStep step in plan.steps)
        {
            output.WriteLine($"Step: {step.name}");
            output.WriteLine($"  Executable: {step.executable}");
            output.WriteLine($"  Working directory: {step.workingDirectory}");
            output.WriteLine("  Arguments:");
            foreach (string argument in step.arguments)
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

    private static string ReadValue(
        string[] args,
        ref int index,
        string option
    ) {
        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
            throw new ArgumentException($"Option '{option}' requires a value.");
        return args[index];
    }

    private static int Fail(
        TextWriter error,
        string message
    ) {
        error.WriteLine($"error: {message}");
        error.WriteLine("Run 'bindgen-cs --help' for usage.");
        return 2;
    }

    private static JsonSerializerOptions jsonOptions { get; } = new()
    {
        WriteIndented = true
    };

    private sealed record Options(
        string? manifestPath,
        string? outputPath,
        string? compilerPath,
        string? buildToolPath,
        string? exportToolPath,
        string? packageRoot,
        string provider,
        int timeoutSeconds,
        bool dryRun,
        bool json,
        bool verifyExports
    );
    private sealed record NativeBuildCommandResult(
        bool success,
        string provider,
        IReadOnlyList<NativeBuildStepResult> steps,
        bool timedOut,
        string outputFile,
        NativeExportInspectionResult? exports,
        NativeAssetLayoutResult? packagedAsset
    );
}
