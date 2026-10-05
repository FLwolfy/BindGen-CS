using System;
using System.Linq;
using BGCS.Tool.Commands;
using BGCS.Tool.Validation;

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
                "doctor" => DoctorCommand.Run(),
                "validate" when args.Length > 1 && args[1] == "api-snapshot" => ApiSnapshotValidator.Run(args[2..], Console.Out, Console.Error),
                "validate" when args.Length > 1 && args[1] == "dependencies" => DependencyAudit.Run(args[2..], Console.Out, Console.Error),
                "validate" when args.Length > 1 && args[1] == "style" => CSharpStyleValidator.Run(args[2..], Console.Out, Console.Error),
                "validate" when args.Length > 1 && args[1] == "architecture" => ArchitectureValidator.Run(args[2..], Console.Out, Console.Error),
                "validate" when args.Length > 1 && args[1] == "documentation" => PublicApiDocumentationValidator.Run(args[2..], Console.Out, Console.Error),
                "validate" => ValidateCommand.Run(args[1..]),
                "inspect" => ValidateCommand.Inspect(args[1..]),
                "generate" => GenerateCommand.Run(args[1..]),
                "build" => GenerateCommand.Build(args[1..]),
                "diff" => GenerateCommand.Diff(args[1..]),
                "workspace" => WorkspaceCommand.Run(args[1..]),
                "schema" => SchemaCommand.Run(args[1..], Environment.CurrentDirectory, Console.Out, Console.Error),
                "explain" => ExplainCommand.Run(args[1..], Console.Out, Console.Error),
                "bridge" => BridgeCommand.Run(args[1..]),
                "native-build" => NativeBuildCommand.Run(args[1..], Environment.CurrentDirectory, Console.Out, Console.Error),
                "supply-chain" => SupplyChainCommand.Run(args[1..], Environment.CurrentDirectory, Console.Out, Console.Error),
                "version" or "--version" => PrintVersion(),
                _ when args[0].EndsWith(".json", StringComparison.OrdinalIgnoreCase) => GenerateCommand.Run(args),
                _ => Fail($"Unknown command '{args[0]}'.")
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            return 2;
        }
    }

    private static bool IsHelp(string argument) => argument is "help" or "-h" or "--help";
    private static int PrintVersion()
    {
        Console.WriteLine(typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown");
        return 0;
    }

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
        Console.WriteLine("  bindgen-cs doctor");
        Console.WriteLine("  bindgen-cs validate [config.json]");
        Console.WriteLine("  bindgen-cs validate api-snapshot <assembly> <output>");
        Console.WriteLine("  bindgen-cs validate dependencies <vulnerabilities|licenses> <report-or-root> [inventory]");
        Console.WriteLine("  bindgen-cs validate style <source-root> [--fix]");
        Console.WriteLine("  bindgen-cs validate architecture <repository-root>");
        Console.WriteLine("  bindgen-cs validate documentation <source-root>");
        Console.WriteLine("  bindgen-cs inspect [config.json] [--json]");
        Console.WriteLine("  bindgen-cs generate [config.json] [--output directory]");
        Console.WriteLine("  bindgen-cs build [config.json] [--output directory]");
        Console.WriteLine("  bindgen-cs diff [config.json] [--output directory]");
        Console.WriteLine("  bindgen-cs workspace <validate|generate|diff> <workspace.json>");
        Console.WriteLine("  bindgen-cs schema [output.json] [--kind c|cpp] [--allow-unknown-properties]");
        Console.WriteLine("  bindgen-cs explain [diagnostic-code] [--json]");
        Console.WriteLine("  bindgen-cs bridge [config.json] [--output directory]");
        Console.WriteLine("  bindgen-cs native-build [bridge.manifest.json] [--provider auto|clang|clang-cl|cmake|meson|msbuild] [--package-root <dir>]");
        Console.WriteLine("  bindgen-cs supply-chain [artifact-directory] [--output <dir>] [--revision <commit>]");
        Console.WriteLine("      [--output library] [--compiler path] [--build-tool path] [--export-tool path]");
        Console.WriteLine("      [--no-verify-exports] [--timeout seconds] [--dry-run] [--json]");
        Console.WriteLine("  bindgen-cs <config.json> [--output directory]");
        Console.WriteLine("  bindgen-cs version");
    }
}
