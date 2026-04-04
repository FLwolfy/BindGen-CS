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
        if (args.Length > 0 && args[0].Equals("--all", StringComparison.OrdinalIgnoreCase))
        {
            return RunAllScenarios(args);
        }

        string configPath = args.Length > 0 ? args[0] : "config.json";
        string outputPath = args.Length > 2 ? args[2] : "Output";
        string? headerPathOverride = args.Length > 1 ? args[1] : null;

        AppRunOptions options = LoadAppRunOptions(configPath);
        List<string> entryFiles = ResolveEntryFiles(options.EntryFiles, headerPathOverride);
        bool useComGenerator = options.UseComGenerator ?? false;

        return RunSingle(configPath, entryFiles, outputPath, useComGenerator, options.OutputFilterFiles);
    }

    private static int RunAllScenarios(string[] args)
    {
        string outputRoot = args.Length > 1 ? args[1] : "Output";
        List<(string Name, string Config, List<string> Headers, bool UseCom)> scenarios =
        [
            ("basic-c", "config.json", [Path.Combine("headers", "basic_c.h")], false),
            ("cpp-extern-c", "config.json", [Path.Combine("headers", "cpp_extern_c.h")], false),
            ("com-like", "config.com.json", [Path.Combine("headers", "com_like.h")], true)
        ];

        int failures = 0;

        foreach (var scenario in scenarios)
        {
            string scenarioOutput = Path.Combine(outputRoot, scenario.Name);
            Console.WriteLine($"== Running scenario: {scenario.Name} ==");
            int code = RunSingle(scenario.Config, scenario.Headers, scenarioOutput, scenario.UseCom, outputFilterFiles: null);
            if (code != 0)
            {
                failures++;
            }
        }

        Console.WriteLine(failures == 0
            ? "All scenarios succeeded."
            : $"Some scenarios failed. Count: {failures}");

        return failures == 0 ? 0 : 1;
    }

    private static int RunSingle(string configPath, List<string> entryFiles, string outputPath, bool useComGenerator, List<string>? outputFilterFiles)
    {
        CsCodeGeneratorConfig config = CsCodeGeneratorConfig.Load(configPath);

        bool success;
        IReadOnlyList<LogMessage> messages;

        if (useComGenerator)
        {
            CsComCodeGenerator generator = new(config);
            success = generator.Generate(entryFiles, outputPath, outputFilterFiles);
            messages = generator.Messages;
        }
        else
        {
            CsCodeGenerator generator = new(config);
            success = generator.Generate(entryFiles, outputPath, outputFilterFiles);
            messages = generator.Messages;
        }

        foreach (var message in messages)
        {
            Console.WriteLine(message);
        }

        Console.WriteLine(success
            ? $"Generation succeeded. Output: {outputPath}"
            : $"Generation failed. Config: {configPath}, EntryFiles: {string.Join(", ", entryFiles)}");

        return success ? 0 : 1;
    }

    private static List<string> ResolveEntryFiles(List<string>? configEntryFiles, string? headerPathOverride)
    {
        if (!string.IsNullOrWhiteSpace(headerPathOverride))
        {
            return [headerPathOverride];
        }

        if (configEntryFiles is { Count: > 0 })
        {
            return configEntryFiles;
        }

        return [Path.Combine("headers", "basic_c.h")];
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

        if (TryReadStringArray(root, "OutputFilterFiles", out var outputFilterFiles) || TryReadStringArray(root, "AllowedHeaders", out outputFilterFiles))
        {
            options.OutputFilterFiles = outputFilterFiles;
        }

        if (root.TryGetProperty("UseComGenerator", out JsonElement useComElement) &&
            (useComElement.ValueKind == JsonValueKind.True || useComElement.ValueKind == JsonValueKind.False))
        {
            options.UseComGenerator = useComElement.GetBoolean();
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

    private sealed class AppRunOptions
    {
        public List<string>? EntryFiles { get; set; }

        public List<string>? OutputFilterFiles { get; set; }

        public bool? UseComGenerator { get; set; }
    }
}
