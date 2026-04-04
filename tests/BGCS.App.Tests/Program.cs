using System;
using System.Collections.Generic;
using System.IO;
using BGCS;
using BGCS.Core.Logging;

namespace BGCS.App.Tests;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("--all", StringComparison.OrdinalIgnoreCase))
        {
            return RunAllScenarios(args);
        }

        string configPath = args.Length > 0 ? args[0] : "config.json";
        string headerPath = args.Length > 1 ? args[1] : Path.Combine("headers", "basic_c.h");
        string outputPath = args.Length > 2 ? args[2] : "Output";

        return RunSingle(configPath, headerPath, outputPath, useComGenerator: false);
    }

    private static int RunAllScenarios(string[] args)
    {
        string outputRoot = args.Length > 1 ? args[1] : "Output";
        List<(string Name, string Config, string Header, bool UseCom)> scenarios =
        [
            ("basic-c", "config.json", Path.Combine("headers", "basic_c.h"), false),
            ("cpp-extern-c", "config.json", Path.Combine("headers", "cpp_extern_c.h"), false),
            ("com-like", "config.com.json", Path.Combine("headers", "com_like.h"), true)
        ];

        int failures = 0;

        foreach (var scenario in scenarios)
        {
            string scenarioOutput = Path.Combine(outputRoot, scenario.Name);
            Console.WriteLine($"== Running scenario: {scenario.Name} ==");
            int code = RunSingle(scenario.Config, scenario.Header, scenarioOutput, scenario.UseCom);
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

    private static int RunSingle(string configPath, string headerPath, string outputPath, bool useComGenerator)
    {
        CsCodeGeneratorConfig config = CsCodeGeneratorConfig.Load(configPath);

        bool success;
        IReadOnlyList<LogMessage> messages;

        if (useComGenerator)
        {
            CsComCodeGenerator generator = new(config);
            success = generator.Generate(headerPath, outputPath);
            messages = generator.Messages;
        }
        else
        {
            CsCodeGenerator generator = new(config);
            success = generator.Generate(headerPath, outputPath);
            messages = generator.Messages;
        }

        foreach (var message in messages)
        {
            Console.WriteLine(message);
        }

        Console.WriteLine(success
            ? $"Generation succeeded. Output: {outputPath}"
            : $"Generation failed. Config: {configPath}, Header: {headerPath}");

        return success ? 0 : 1;
    }
}
