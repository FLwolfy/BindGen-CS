using BGCS.Contracts.Inputs;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using BGCS.Frontend.Clang;
using BGCS.Contracts.Requests;
using BGCS.Platforms.Targeting;

namespace BGCS.Tests.ParserLibraryHost;

internal static class Program
{
    private static int Main(string[] arguments)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-library-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "native.h");
            File.WriteAllText(header, "int bgcs_library_add(int left, int right);");
            FrontendResult parsed = new ClangFrontend().ParseAsync(new(new TargetResolver().Resolve(new(new("host"))), SourceLanguage.C, [header])
                { pathBase = root, parseMacros = false }, CancellationToken.None).GetAwaiter().GetResult();
            if (parsed.module.diagnostics.Any(diagnostic => diagnostic.isFailure))
            {
                Console.Error.WriteLine(string.Join(Environment.NewLine, parsed.module.diagnostics.Select(diagnostic => diagnostic.message)));
                return 1;
            }
            File.WriteAllText(arguments.Single(), parsed.module.declarations.Single(declaration => declaration.kind == "Function").name);
            return 0;
        }
        catch (Exception failure)
        {
            Console.Error.WriteLine(failure);
            return 1;
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
