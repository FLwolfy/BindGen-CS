using System;

namespace BGCS.Tool.Commands;

internal static class GenerationArguments
{
    private const string C_DEFAULT_CONFIG = "bindgen.json";
    internal static (string configPath, string? outputPath) Parse(string[] args)
    {
        string configPath = C_DEFAULT_CONFIG;
        string? outputPath = null;
        bool positionalConfigRead = false;
        for (int i = 0; i < args.Length; i++)
        {
            string argument = args[i];
            if (argument is "-c" or "--config")
            {
                configPath = ReadValue(args, ref i, argument);
            }
            else if (argument is "-o" or "--output")
            {
                outputPath = ReadValue(args, ref i, argument);
            }
            else if (!argument.StartsWith('-') && !positionalConfigRead)
            {
                configPath = argument;
                positionalConfigRead = true;
            }
            else
            {
                throw new ArgumentException($"Unknown generate option '{argument}'.");
            }
        }

        return (configPath, outputPath);
    }

    internal static string ParseConfiguration(string[] args)
    {
        if (args.Length == 0)
            return C_DEFAULT_CONFIG;
        if (args.Length == 1 && !args[0].StartsWith('-'))
            return args[0];
        if (args.Length == 2 && args[0] is "-c" or "--config")
            return args[1];
        throw new ArgumentException("Expected an optional config path or '--config <path>'.");
    }

    private static string ReadValue(
        string[] args,
        ref int index,
        string option
    ) {
        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
        {
            throw new ArgumentException($"Option '{option}' requires a value.");
        }

        return args[index];
    }
}
