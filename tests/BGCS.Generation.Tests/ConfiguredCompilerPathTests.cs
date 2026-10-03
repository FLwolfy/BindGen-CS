using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

using BGCS.Configuration;
using BGCS.CppAst.Parsing;
using BGCS.CppAst.Targeting;
using Xunit;

namespace BGCS.Tests;

/// <summary>
/// Verifies configured compiler paths through the generator's supported parser configuration boundary.
/// </summary>
public sealed class ConfiguredCompilerPathTests
{
    /// <summary>
    /// Verifies that compiler environment variables preserve configuration-relative path semantics.
    /// </summary>
    /// <param name="relative">
    /// Whether the environment variable contains a path relative to the configuration directory.
    /// </param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompilerEnvironmentVariableUsesConfigurationDirectory(bool relative)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-configured-driver-" + Guid.NewGuid().ToString("N"));
        string variable = "BGCS_TEST_COMPILER_" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(root);
        try
        {
            string sdk = Path.Combine(root, "sdk");
            Directory.CreateDirectory(sdk);
            string compiler = BuildCompiler(root, sdk);
            Environment.SetEnvironmentVariable(variable, relative ? Path.GetRelativePath(root, compiler) : compiler);
            string configFile = Path.Combine(root, "bindgen.json");
            File.WriteAllText(configFile,
                "{\"ApiName\":\"Probe\",\"Namespace\":\"BGCS.Test\",\"ParserKind\":\"C\",\"CompilerPath\":\"%"
                + variable + "%\"}");
            var generator = new ParserConfigurationProbe(new ConfigLoader().Load(configFile));

            Assert.Contains(sdk, generator.CaptureSettings().SystemIncludeFolders);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
            Directory.Delete(root, recursive: true);
        }
    }

    private static string BuildCompiler(
        string root,
        string sdk
    ) {
        string driver = CppToolchainDiscovery.FindCompiler(CppParserKind.C)
            ?? throw new InvalidOperationException("A C compiler is required for configuration boundary tests.");
        string compilerRoot = Path.Combine(root, "toolchain");
        Directory.CreateDirectory(compilerRoot);
        string compiler = Path.Combine(compilerRoot, OperatingSystem.IsWindows() ? "query.exe" : "query");
        string source = Path.Combine(root, "query.c");
        string sdkLiteral = sdk.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        File.WriteAllText(source, $$"""
            #include <stdio.h>
            #include <string.h>
            int main(int argc, char** argv) {
                if (argc > 1 && strcmp(argv[1], "-print-resource-dir") == 0) return 0;
                fputs("#include <...> search starts here:\n", stderr);
                fputs("{{sdkLiteral}}\n", stderr);
                fputs("End of search list.\n", stderr);
                return 0;
            }
            """);
        var start = new ProcessStartInfo(driver)
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (string argument in new[] { source, "-o", compiler })
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }
        Task.WaitAll(output, error);
        Assert.True(process.ExitCode == 0, output.Result + error.Result);
        return compiler;
    }

    private sealed class ParserConfigurationProbe(CsCodeGeneratorConfig config) : CsCodeGenerator(config)
    {
        internal CppParserOptions CaptureSettings() => PrepareSettings();
    }
}
