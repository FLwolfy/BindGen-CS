using BGCS.Contracts.Inputs;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using BGCS.Frontend.Clang;
using BGCS.Contracts.Requests;
using BGCS.Platforms.Targeting;
using Microsoft.Build.Framework;

namespace BGCS.Tests.ParserLibraryHost;

/// <summary>
/// Exercises the neutral frontend implementation when loaded by an unrelated MSBuild host.
/// </summary>
public sealed class ParseNativeApiTask : Microsoft.Build.Utilities.Task
{
    /// <summary>
    /// Gets or sets the path receiving the parsed function's name.
    /// </summary>
    [Required]
    public string outputPath { get; set; } = string.Empty;

    /// <inheritdoc />
    public override bool Execute()
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
                Log.LogError(string.Join(Environment.NewLine, parsed.module.diagnostics.Select(diagnostic => diagnostic.message)));
                return false;
            }
            File.WriteAllText(outputPath, parsed.module.declarations.Single(declaration => declaration.kind == "Function").name);
            return true;
        }
        catch (Exception failure)
        {
            Log.LogErrorFromException(failure, showStackTrace: true);
            return false;
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
