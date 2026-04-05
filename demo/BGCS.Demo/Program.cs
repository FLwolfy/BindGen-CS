using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BGCS;
using BGCS.Core.Logging;

namespace BGCS.Demo;

internal static class Program
{
    private static int Main(string[] args)
    {
        string configPath = args.Length > 0 ? args[0] : "config.runtime-generated.json";
        
        string outputPath = args.Length > 1 ? args[1] : "Output";
        CsCodeGeneratorConfig config = CsCodeGeneratorConfig.Load(configPath);
        AppRunOptions options = LoadAppRunOptions(configPath);

        CsCodeGenerator generator = new(config);
        List<string> entryFiles = options.EntryFiles is { Count: > 0 }
            ? options.EntryFiles
            : [Path.Combine("headers", "basic_c.h")];
        bool success = generator.Generate(entryFiles, outputPath, options.AllowedHeaders);
        IReadOnlyList<LogMessage> messages = generator.Messages;

        foreach (var message in messages)
        {
            Console.WriteLine(message);
        }

        Console.WriteLine(success
            ? $"Generation succeeded. Output: {outputPath}"
            : $"Generation failed. Config: {configPath}, EntryFiles: {string.Join(", ", entryFiles)}");

        if (!success)
        {
            return 1;
        }

        bool runtimeCheckPassed = ValidateRuntimeOutput(config, outputPath);
        return runtimeCheckPassed ? 0 : 2;
    }

    private static AppRunOptions LoadAppRunOptions(string configPath)
    {
        if (!File.Exists(configPath))
        {
            return new AppRunOptions();
        }

        using FileStream stream = File.OpenRead(configPath);
        using JsonDocument doc = JsonDocument.Parse(stream);
        JsonElement root = doc.RootElement;

        AppRunOptions options = new();
        if (TryReadStringArray(root, "EntryFiles", out var entryFiles) || TryReadStringArray(root, "Headers", out entryFiles))
        {
            options.EntryFiles = entryFiles;
        }

        if (TryReadStringArray(root, "allowedHeaders", out var allowedHeaders)
            || TryReadStringArray(root, "AllowedHeaders", out allowedHeaders)
            || TryReadStringArray(root, "OutputFilterFiles", out allowedHeaders))
        {
            options.AllowedHeaders = allowedHeaders;
        }

        return options;
    }

    private static bool TryReadStringArray(JsonElement root, string propertyName, out List<string> values)
    {
        values = [];
        if (!root.TryGetProperty(propertyName, out JsonElement element) || element.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        values = element
            .EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToList();

        return true;
    }

    private static bool ValidateRuntimeOutput(CsCodeGeneratorConfig config, string outputPath)
    {
        string runtimeNamespace = GetRuntimeNamespace();
        string runtimeFile = Path.Combine(outputPath, "Runtime.cs");
        if (config.GenerateRuntimeSource)
        {
            if (!File.Exists(runtimeFile))
            {
                Console.WriteLine("Runtime check failed: GenerateRuntimeSource=true requires Runtime.cs.");
                return false;
            }

            string runtimeText = File.ReadAllText(runtimeFile);
            bool runtimeFileValid = runtimeText.Contains($"namespace {runtimeNamespace}", StringComparison.Ordinal);
            if (!runtimeFileValid)
            {
                Console.WriteLine("Runtime check failed: Runtime.cs does not use the expected runtime namespace.");
                return false;
            }

            Console.WriteLine("Runtime check passed: runtime is generated as standalone Runtime.cs.");
            return true;
        }

        if (File.Exists(runtimeFile))
        {
            Console.WriteLine("Runtime check failed: GenerateRuntimeSource=false must not emit Runtime.cs.");
            return false;
        }

        Console.WriteLine("Runtime check passed: runtime source generation is disabled.");
        return true;
    }

    private static string GetRuntimeNamespace() => "BGCS.Runtime";
    private sealed class AppRunOptions
    {
        public List<string>? EntryFiles { get; set; }

        public List<string>? AllowedHeaders { get; set; }
    }
}
