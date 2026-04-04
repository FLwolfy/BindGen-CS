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
        string configPath = args.Length > 0 ? args[0] : "config.no-runtime.json";
        // string configPath = args.Length > 0 ? args[0] : "config.need-runtime.json";
        // string configPath = args.Length > 0 ? args[0] : "config.embed-runtime.json";
        
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

        bool runtimeCheckPassed = ValidateRuntimeEmbedding(config, outputPath);
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

    private static bool ValidateRuntimeEmbedding(CsCodeGeneratorConfig config, string outputPath)
    {
        string mergedPath = Path.Combine(outputPath, string.IsNullOrWhiteSpace(config.SingleFileOutputName) ? "Bindings.cs" : config.SingleFileOutputName);
        string scanText;

        if (File.Exists(mergedPath))
        {
            scanText = File.ReadAllText(mergedPath);
        }
        else
        {
            string[] files = Directory.Exists(outputPath)
                ? Directory.GetFiles(outputPath, "*.cs", SearchOption.AllDirectories)
                : [];
            scanText = string.Join(Environment.NewLine, files.Select(File.ReadAllText));
        }

        string runtimeNamespace = GetRuntimeNamespace(config);
        string runtimeUsing = $"using {runtimeNamespace};";
        string guardSymbol = BuildRuntimeIncludeGuardSymbol(runtimeNamespace);

        if (scanText.Contains("using BGCS.Runtime;", StringComparison.Ordinal))
        {
            Console.WriteLine("Runtime check failed: generated output must not contain `using BGCS.Runtime;`.");
            return false;
        }

        bool needsRuntime = scanText.Contains(runtimeUsing, StringComparison.Ordinal)
            || scanText.Contains("FunctionTable", StringComparison.Ordinal)
            || scanText.Contains("[NativeName", StringComparison.Ordinal)
            || scanText.Contains("Bitfield.", StringComparison.Ordinal)
            || scanText.Contains("Pointer<", StringComparison.Ordinal)
            || scanText.Contains("ConstPointer<", StringComparison.Ordinal);

        if (!needsRuntime)
        {
            Console.WriteLine("Runtime check passed: this binding does not require runtime.");
            return true;
        }

        if (config.IncludeRuntimeSourceInSingleFile)
        {
            bool hasEmbeddedRuntimeCode = File.Exists(mergedPath)
                && File.ReadAllText(mergedPath).Contains($"namespace {runtimeNamespace}", StringComparison.Ordinal)
                && File.ReadAllText(mergedPath).Contains($"#if !{guardSymbol}", StringComparison.Ordinal);
            if (!hasEmbeddedRuntimeCode)
            {
                Console.WriteLine("Runtime check failed: when IncludeRuntimeSourceInSingleFile=true, Bindings.cs must include runtime namespace and include guard.");
                return false;
            }

            Console.WriteLine("Runtime check passed: runtime is embedded in Bindings.cs with namespace `config.Namespace + .Runtime`.");
            return true;
        }

        string runtimeFile = Path.Combine(outputPath, "Runtime.cs");
        if (!File.Exists(runtimeFile))
        {
            Console.WriteLine("Runtime check failed: when IncludeRuntimeSourceInSingleFile=false and runtime is required, a standalone Runtime.cs must be generated.");
            return false;
        }

        string runtimeText = File.ReadAllText(runtimeFile);
        bool runtimeFileValid =
            runtimeText.Contains($"namespace {runtimeNamespace}", StringComparison.Ordinal)
            && runtimeText.Contains($"#if !{guardSymbol}", StringComparison.Ordinal);
        if (!runtimeFileValid)
        {
            Console.WriteLine("Runtime check failed: Runtime.cs does not use the expected namespace/include guard.");
            return false;
        }

        Console.WriteLine("Runtime check passed: runtime is generated as standalone Runtime.cs with namespace `config.Namespace + .Runtime`.");
        return true;
    }

    private static string GetRuntimeNamespace(CsCodeGeneratorConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Namespace))
        {
            return "Generated.Runtime";
        }

        return $"{config.Namespace}.Runtime";
    }

    private static string BuildRuntimeIncludeGuardSymbol(string runtimeNamespace)
    {
        List<char> chars = new(runtimeNamespace.Length + 24);
        chars.AddRange("BGCS_RUNTIME_INCLUDED_");
        for (int i = 0; i < runtimeNamespace.Length; i++)
        {
            char c = runtimeNamespace[i];
            chars.Add(char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : '_');
        }
        return new string([.. chars]);
    }

    private sealed class AppRunOptions
    {
        public List<string>? EntryFiles { get; set; }

        public List<string>? AllowedHeaders { get; set; }
    }
}
