using System;
using BGCS.RepositoryTools.Commands;
using BGCS.RepositoryTools.Validation;
namespace BGCS.RepositoryTools;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            return args switch
            {
                ["validate", "api-snapshot", .. var rest] => ApiSnapshotValidator.Run(rest, Console.Out, Console.Error),
                ["validate", "dependencies", .. var rest] => DependencyAudit.Run(rest, Console.Out, Console.Error),
                ["validate", "style", .. var rest] => CSharpStyleValidator.Run(rest, Console.Out, Console.Error),
                ["validate", "architecture", .. var rest] => ArchitectureValidator.Run(rest, Console.Out, Console.Error),
                ["validate", "documentation", .. var rest] => PublicApiDocumentationValidator.Run(rest, Console.Out, Console.Error),
                ["supply-chain", .. var rest] => SupplyChainCommand.Run(rest, Environment.CurrentDirectory, Console.Out, Console.Error),
                _ => Fail("Expected validate <api-snapshot|dependencies|style|architecture|documentation> or supply-chain.")
            };
        }
        catch (Exception exception) { return Fail(exception.Message); }
    }
    private static int Fail(string message) { Console.Error.WriteLine(message); return 2; }
}
