using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BGCS.Core.Logging;
using BGCS.CppAst.Parsing;
using Xunit;

namespace BGCS.Tests;

public class DelegatePointerConsistencyTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Generate_CallbackTypes_ShouldRespectDelegatesAsVoidPointerGlobally(bool delegatesAsVoidPointer)
    {
        string header = """
            typedef void (*MyCallback)(int value);

            typedef struct MyContainer
            {
                MyCallback callback;
            } MyContainer;

            void Bgcs_SetCallback(MyCallback callback);
            MyCallback Bgcs_GetCallback(void);
            """;

        var run = RunGenerator(header, delegatesAsVoidPointer);
        try
        {
            Assert.True(run.Ok);
            Assert.DoesNotContain(run.Messages, x => x.Severtiy is LogSeverity.Error or LogSeverity.Critical);

            if (delegatesAsVoidPointer)
            {
                Assert.DoesNotContain("delegate*<", run.GeneratedCode, StringComparison.Ordinal);
                Assert.Contains("void* callback", run.GeneratedCode, StringComparison.Ordinal);
            }
            else
            {
                Assert.Contains("delegate*<int, void>", run.GeneratedCode, StringComparison.Ordinal);
                Assert.DoesNotContain("void* callback", run.GeneratedCode, StringComparison.Ordinal);
            }
        }
        finally
        {
            Cleanup(run.TempDirectory);
        }
    }

    private static (bool Ok, string TempDirectory, string GeneratedCode, IReadOnlyList<LogMessage> Messages) RunGenerator(string headerText, bool delegatesAsVoidPointer)
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-delegate-strategy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        string headerPath = Path.Combine(temp, "input.h");
        string outputPath = Path.Combine(temp, "out");
        File.WriteAllText(headerPath, headerText);

        CsCodeGeneratorConfig cfg = new()
        {
            ApiName = "DelegateApi",
            Namespace = "Delegate.Generated",
            LibName = "delegatetest",
            GenerateExtensions = false,
            ImportType = ImportType.DllImport,
            DelegatesAsVoidPointer = delegatesAsVoidPointer
        };

        CsCodeGenerator generator = new(cfg);
        CppParserOptions parserOptions = new()
        {
            ParseMacros = true,
            ParseComments = true,
            ParseSystemIncludes = false,
            ParseCommentAttribute = true,
            ParserKind = CppParserKind.Cpp,
            AutoSquashTypedef = false
        };
        parserOptions.AdditionalArguments.Add("-undef");

        bool ok = generator.Generate(parserOptions, headerPath, outputPath);
        string generatedCode = string.Empty;
        if (Directory.Exists(outputPath))
        {
            string[] files = Directory.GetFiles(outputPath, "*.cs", SearchOption.AllDirectories);
            generatedCode = string.Join("\n\n", files.Select(File.ReadAllText));
        }

        return (ok, temp, generatedCode, generator.Messages);
    }

    private static void Cleanup(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
