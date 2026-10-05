using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BGCS.Configuration;
using BGCS.Core.Logging;
using BGCS.CppAst.Parsing;
using BGCS.Facade;
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
            Assert.DoesNotContain(run.Messages, x => x.severity is LogSeverity.Error or LogSeverity.Critical);

            if (delegatesAsVoidPointer)
            {
                Assert.DoesNotContain("delegate*", run.GeneratedCode, StringComparison.Ordinal);
                Assert.Contains("void* callback", run.GeneratedCode, StringComparison.Ordinal);
            }
            else
            {
                Assert.Contains("delegate* unmanaged[Cdecl]<int, void>", run.GeneratedCode, StringComparison.Ordinal);
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
            apiName = "DelegateApi",
            @namespace = "Delegate.Generated",
            libName = "delegatetest",
            generateExtensions = false,
            importType = ImportType.DllImport,
            delegatesAsVoidPointer = delegatesAsVoidPointer
        };

        CsCodeGenerator generator = new(cfg);
        CppParserOptions parserOptions = new()
        {
            parseMacros = true,
            parseComments = true,
            parseSystemIncludes = false,
            parseCommentAttribute = true,
            parserKind = CppParserKind.Cpp,
            autoSquashTypedef = false
        };
        parserOptions.additionalArguments.Add("-undef");

        bool ok = generator.Generate(parserOptions, headerPath, outputPath);
        string generatedCode = string.Empty;
        if (Directory.Exists(outputPath))
        {
            string[] files = Directory.GetFiles(outputPath, "*.cs", SearchOption.AllDirectories);
            generatedCode = string.Join("\n\n", files.Select(File.ReadAllText));
        }

        return (ok, temp, generatedCode, generator.messages);
    }

    private static void Cleanup(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
