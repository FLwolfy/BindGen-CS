using System;
using System.IO;
using System.Linq;
using BGCS.CppAst.Parsing;
using Microsoft.Build.Framework;

namespace BGCS.Tests.ParserLibraryHost;

/// <summary>
/// Exercises the public parser when loaded as a library by an unrelated MSBuild host.
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
        try
        {
            var compilation = CppParser.Parse("int bgcs_library_add(int left, int right);",
                new CppParserOptions { parserKind = CppParserKind.C });
            if (compilation.hasErrors)
            {
                Log.LogError(compilation.diagnostics.ToString());
                return false;
            }
            File.WriteAllText(outputPath, compilation.functions.Single().name);
            return true;
        }
        catch (Exception failure)
        {
            Log.LogErrorFromException(failure, showStackTrace: true);
            return false;
        }
    }
}
