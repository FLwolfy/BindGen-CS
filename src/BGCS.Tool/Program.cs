using System;
using System.Linq;
using BGCS.Tool.Commands;

namespace BGCS.Tool;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args.Any(IsHelp))
            {
                PrintHelp();
                return 0;
            }

            return args[0].ToLowerInvariant() switch
            {
                "init" => InitCommand.Run(args[1..], Environment.CurrentDirectory, Console.Out, Console.Error),
                "doctor" => DoctorCommand.Run(args[1..]),
                "validate" => ValidateCommand.Run(args[1..]),
                "inspect" => ValidateCommand.Inspect(args[1..]),
                "generate" => GenerateCommand.Run(args[1..]),
                "diff" => GenerateCommand.Diff(args[1..]),
                "workspace" => WorkspaceCommand.Run(args[1..]),
                "schema" => SchemaCommand.Run(args[1..], Environment.CurrentDirectory, Console.Out, Console.Error),
                "explain" => ExplainCommand.Run(args[1..], Console.Out, Console.Error),
                "bridge" => BridgeCommand.Run(args[1..]),
                "version" or "--version" => VersionCommand.Run(args[1..], Console.Out),
                _ when args[0].EndsWith(".json", StringComparison.OrdinalIgnoreCase) => GenerateCommand.Run(args),
                _ => Fail($"Unknown command '{args[0]}'.")
            };
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("error: BGCS execution was cancelled.");
            return 130;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            return 2;
        }
    }

    private static bool IsHelp(string argument) => argument is "help" or "-h" or "--help";
    private static int Fail(string message)
    {
        Console.Error.WriteLine($"error: {message}");
        Console.Error.WriteLine("Run 'bindgen-cs --help' for usage.");
        return 2;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("BindGen-CS");
        Console.WriteLine("  bindgen-cs init [config.json|header.h|header.hpp] [--language auto|c|cpp] [--config output.json]");
        Console.WriteLine("  bindgen-cs doctor [bindgen.json] [--json]");
        Console.WriteLine("  bindgen-cs validate [config.json]");
        Console.WriteLine("  bindgen-cs inspect [config.json] [--json]");
        Console.WriteLine("  bindgen-cs generate [config.json] [--output directory]");
        Console.WriteLine("  bindgen-cs diff [config.json] [--output directory]");
        Console.WriteLine("  bindgen-cs workspace <validate|generate|diff> <workspace.json> [--json]");
        Console.WriteLine("  bindgen-cs schema [output.json] [--kind binding|workspace|plugin]");
        Console.WriteLine("  bindgen-cs explain [diagnostic-code] [--json]");
        Console.WriteLine("  bindgen-cs bridge [config.json] [--output directory]");
        Console.WriteLine("  bindgen-cs <config.json> [--output directory]");
        Console.WriteLine("  bindgen-cs version [--json]");
        Console.WriteLine("  Generation commands accept --profile <name>, --target <configured-id>, --output <directory> and --json.");
        Console.WriteLine("  Host commands accept --log-level <severity> and --frontend-log-level <severity>; failure diagnostics always remain visible.");
    }
}
