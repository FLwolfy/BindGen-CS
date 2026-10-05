using System;
using System.Collections.Generic;
using System.IO;
using BGCS.Configuration;
using BGCS.Core.Logging;
using BGCS.CppAst.Parsing;
using BGCS.Facade;
using BGCS.Metadata;
using BGCS.Patching;
using Xunit;

namespace BGCS.Tests;

public class SingleFilePatchCompatibilityTests
{
    [Fact]
    public void Generate_MergedWithoutRuntimeSource_PostPatchChangesShouldSurviveMerge()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-singlefile-patch-embedded-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        string headerPath = Path.Combine(temp, "input.h");
        string outputPath = Path.Combine(temp, "out");
        File.WriteAllText(headerPath, "int Bgcs_Sum(int a, int b);");

        try
        {
            CsCodeGeneratorConfig config = CreateSingleFileConfig(generateRuntimeSource: false);
            CsCodeGenerator generator = new(config);
            generator.patchEngine.RegisterPostPatch(new ReplaceFunctionSymbolPostPatch("BgcsSumNative", "BgcsSumNativePatched"));

            bool ok = generator.Generate(CreateParserOptions(), headerPath, outputPath);

            Assert.True(ok);
            Assert.DoesNotContain(generator.messages, x => x.severity is LogSeverity.Error or LogSeverity.Critical);

            string mergedPath = Path.Combine(outputPath, "Bindings.cs");
            Assert.True(File.Exists(mergedPath));

            string mergedText = File.ReadAllText(mergedPath);
            Assert.Contains("BgcsSumNativePatched", mergedText, StringComparison.Ordinal);
            Assert.Contains("using BGCS.Runtime;", mergedText, StringComparison.Ordinal);
            Assert.DoesNotContain("namespace BGCS.Runtime", mergedText, StringComparison.Ordinal);
            Assert.DoesNotContain("#if !BGCS_RUNTIME_EXTERNAL", mergedText, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(outputPath, "Runtime.cs")));

            string[] csFiles = Directory.GetFiles(outputPath, "*.cs", SearchOption.AllDirectories);
            Assert.Single(csFiles);
            Assert.Equal(mergedPath, csFiles[0]);
        }
        finally
        {
            Cleanup(temp);
        }
    }

    [Fact]
    public void Generate_MergedWithRuntimeSource_PostPatchChangesShouldSurviveMerge()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-singlefile-patch-runtimefile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        string headerPath = Path.Combine(temp, "input.h");
        string outputPath = Path.Combine(temp, "out");
        File.WriteAllText(headerPath, "int Bgcs_Sum(int a, int b);");

        try
        {
            CsCodeGeneratorConfig config = CreateSingleFileConfig(generateRuntimeSource: true);
            CsCodeGenerator generator = new(config);
            generator.patchEngine.RegisterPostPatch(new ReplaceFunctionSymbolPostPatch("BgcsSumNative", "BgcsSumNativePatched"));

            bool ok = generator.Generate(CreateParserOptions(), headerPath, outputPath);

            Assert.True(ok);
            Assert.DoesNotContain(generator.messages, x => x.severity is LogSeverity.Error or LogSeverity.Critical);

            string mergedPath = Path.Combine(outputPath, "Bindings.cs");
            Assert.True(File.Exists(mergedPath));

            string mergedText = File.ReadAllText(mergedPath);
            Assert.Contains("BgcsSumNativePatched", mergedText, StringComparison.Ordinal);

            string runtimePath = Path.Combine(outputPath, "Runtime.cs");
            Assert.True(File.Exists(runtimePath));

            string runtimeText = File.ReadAllText(runtimePath);
            Assert.Contains("namespace BGCS.Runtime", runtimeText, StringComparison.Ordinal);
            Assert.Contains("using BGCS.Runtime;", mergedText, StringComparison.Ordinal);
            Assert.Contains("#if !BGCS_RUNTIME_EXTERNAL", runtimeText, StringComparison.Ordinal);

            string[] csFiles = Directory.GetFiles(outputPath, "*.cs", SearchOption.AllDirectories);
            Assert.Equal(2, csFiles.Length);
            Assert.Contains(mergedPath, csFiles);
            Assert.Contains(runtimePath, csFiles);
        }
        finally
        {
            Cleanup(temp);
        }
    }

    private static CsCodeGeneratorConfig CreateSingleFileConfig(bool generateRuntimeSource)
    {
        return new CsCodeGeneratorConfig
        {
            apiName = "TestApi",
            @namespace = "BGCS.Tests.Generated",
            libName = "test",
            importType = ImportType.FunctionTable,
            useCustomContext = false,
            generateExtensions = false,
            delegatesAsVoidPointer = false,
            mergeGeneratedFilesToSingleFile = true,
            generateRuntimeSource = generateRuntimeSource
        };
    }

    private static CppParserOptions CreateParserOptions()
    {
        CppParserOptions options = new()
        {
            parseMacros = true,
            parseComments = true,
            parseSystemIncludes = false,
            parseCommentAttribute = true,
            parserKind = CppParserKind.Cpp,
            autoSquashTypedef = false
        };

        options.additionalArguments.Add("-undef");
        return options;
    }

    private static void Cleanup(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    private sealed class ReplaceFunctionSymbolPostPatch : IPostPatch
    {
        private readonly string oldValue;
        private readonly string newValue;

        public ReplaceFunctionSymbolPostPatch(string oldValue, string newValue)
        {
            this.oldValue = oldValue;
            this.newValue = newValue;
        }

        public void Apply(PatchContext context, CsCodeGeneratorMetadata metadata, List<string> files)
        {
            string? targetPath = null;
            for (int i = 0; i < files.Count; i++)
            {
                string file = files[i];
                if (!file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string text = context.ReadFile(file);
                if (text.Contains(oldValue, StringComparison.Ordinal))
                {
                    targetPath = file;
                    break;
                }
            }

            if (targetPath == null)
            {
                return;
            }

            string targetText = context.ReadFile(targetPath);
            context.WriteFile(targetPath, targetText.Replace(oldValue, newValue, StringComparison.Ordinal));
        }

    }
}
