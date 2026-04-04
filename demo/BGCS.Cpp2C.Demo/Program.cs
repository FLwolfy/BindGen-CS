using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BGCS.Cpp2C;

namespace BGCS.Cpp2C.Demo;

internal static class Program
{
    private static int Main(string[] args)
    {
        string configPath = args.Length > 0 ? args[0] : "config.json";
        string outputPath = args.Length > 1 ? args[1] : "Output";

        AppRunOptions options = LoadRunOptions(configPath);
        List<string> entryFiles = options.EntryFiles.Count != 0
            ? options.EntryFiles
            : [Path.Combine("include", "demo.hpp")];

        Cpp2CGeneratorConfig config = Cpp2CGeneratorConfig.Load(configPath);
        Cpp2CCodeGenerator generator = new(config);
        generator.Generate(entryFiles, outputPath, options.OutputFilterFiles.Count == 0 ? null : options.OutputFilterFiles);

        foreach (var message in generator.Messages)
        {
            Console.WriteLine(message);
        }

        bool hasErrors = generator.Messages.Any(x => x.Severtiy is BGCS.Core.Logging.LogSeverity.Error or BGCS.Core.Logging.LogSeverity.Critical);
        Console.WriteLine(hasErrors ? "Cpp2C generation failed." : $"Cpp2C generation succeeded. Output: {outputPath}");
        return hasErrors ? 1 : 0;
    }

    private static AppRunOptions LoadRunOptions(string configPath)
    {
        if (!File.Exists(configPath))
        {
            return new AppRunOptions();
        }

        using FileStream stream = File.OpenRead(configPath);
        using JsonDocument doc = JsonDocument.Parse(stream);
        JsonElement root = doc.RootElement;

        AppRunOptions options = new();
        if (TryReadStringArray(root, "EntryFiles", out var entryFiles))
        {
            options.EntryFiles = entryFiles;
        }

        if (TryReadStringArray(root, "OutputFilterFiles", out var outputFilterFiles))
        {
            options.OutputFilterFiles = outputFilterFiles;
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
        public List<string> EntryFiles { get; set; } = [];

        public List<string> OutputFilterFiles { get; set; } = [];
    }
}
