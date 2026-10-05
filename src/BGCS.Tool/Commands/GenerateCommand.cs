using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BGCS.Configuration;
using BGCS.Core.Execution;
using BGCS.Emission;
using BGCS.Facade;
using BGCS.Tool.Output;

namespace BGCS.Tool.Commands;

internal static class GenerateCommand
{
    internal static int Run(string[] args)
    {
        (string configPath, string? outputPath) = GenerationArguments.Parse(args);
        CsCodeGenerator generator = CsCodeGenerator.Create(configPath);
        using var logging = GenerationDiagnosticWriter.Attach(generator, Console.Out);
        bool success = generator.GenerateConfigured(outputPath);
        if (!success)
        {
            GenerationDiagnosticWriter.WriteFailure(generator.lastResult, Console.Error);
            return 1;
        }

        Console.WriteLine($"Generated bindings from {Path.GetFullPath(configPath)}");
        return 0;
    }

    internal static int Build(string[] args)
    {
        (string configPath, string? outputPath) = GenerationArguments.Parse(args);
        CsCodeGenerator generator = CsCodeGenerator.Create(configPath);
        using var logging = GenerationDiagnosticWriter.Attach(generator, Console.Out);
        if (!generator.GenerateConfigured(outputPath) || generator.lastResult?.module == null)
        {
            GenerationDiagnosticWriter.WriteFailure(generator.lastResult, Console.Error);
            return 1;
        }

        string temp = Path.Combine(Path.GetTempPath(), "bindgen-cs-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            foreach (string sourceFile in generator.lastResult.outputFiles.Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
                File.Copy(sourceFile, Path.Combine(temp, Path.GetFileName(sourceFile)), true);
            if (!File.Exists(Path.Combine(temp, "Runtime.cs")))
                new RuntimeEmitter().Emit(generator.lastResult.module, new(temp, true, "Runtime.cs"));
            File.WriteAllText(Path.Combine(temp, "GeneratedBindings.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net9.0</TargetFramework>
                    <ImplicitUsings>disable</ImplicitUsings>
                    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
                    <DisableRuntimeMarshalling>true</DisableRuntimeMarshalling>
                    <Nullable>enable</Nullable>
                    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                  </PropertyGroup>
                </Project>
                """);
            ProcessExecutionResult result = ProcessExecutor.ExecuteAsync(
                DotNetHostDiscovery.FindOrThrow(),
                ["build", "GeneratedBindings.csproj", "--configuration", "Release", "--nologo",
                    "--disable-build-servers", "-m:1", "-nodeReuse:false"],
                temp, TimeSpan.FromMinutes(10)).GetAwaiter().GetResult();
            Console.Write(result.standardOutput);
            if (!string.IsNullOrWhiteSpace(result.standardError))
                Console.Error.Write(result.standardError);
            if (result.timedOut)
                throw new TimeoutException("Generated bindings compilation exceeded its ten-minute deadline.");
            if (result.exitCode != 0)
                return 1;
            Console.WriteLine("Generated bindings build validation passed.");
            return 0;
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    internal static int Diff(string[] args)
    {
        (string configPath, string? outputPath) = GenerationArguments.Parse(args);
        string fullConfigPath = Path.GetFullPath(configPath);
        CsCodeGeneratorConfig config = new BGCS.Configuration.ConfigLoader().Load(fullConfigPath);
        string baseDirectory = Path.GetDirectoryName(fullConfigPath)!;
        string expectedOutput = Path.GetFullPath(outputPath ?? config.outputPath, baseDirectory);
        string temp = Path.Combine(Path.GetTempPath(), "bindgen-cs-diff-" + Guid.NewGuid().ToString("N"));
        try
        {
            CsCodeGenerator generator = new(config);
            if (!generator.GenerateConfigured(temp))
                return 2;
            Dictionary<string, string> expected = ReadDirectory(temp);
            Dictionary<string, string> actual = Directory.Exists(expectedOutput) ? ReadDirectory(expectedOutput) : [];
            List<string> changes = [];
            foreach (string path in expected.Keys.Except(actual.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(path => path))
                changes.Add($"Added: {path}");
            foreach (string path in actual.Keys.Except(expected.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(path => path))
                changes.Add($"Removed: {path}");
            foreach (string path in expected.Keys.Intersect(actual.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(path => path))
                if (!GeneratedSourceComparison.Equals(path, expected[path], actual[path]))
                    changes.Add($"Changed: {path}");
            if (changes.Count == 0)
            {
                Console.WriteLine("Generated bindings are up to date.");
                return 0;
            }

            foreach (string change in changes)
                Console.WriteLine(change);
            return 1;
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    private static Dictionary<string, string> ReadDirectory(string directory)
    {
        return Directory.GetFiles(directory, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(directory, path).Replace('\\', '/'), File.ReadAllText, StringComparer.OrdinalIgnoreCase);
    }
}
