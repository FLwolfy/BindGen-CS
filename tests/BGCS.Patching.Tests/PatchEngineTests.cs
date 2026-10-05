using System;
using System.Collections.Generic;
using System.IO;
using BGCS.Analysis;
using BGCS.Configuration;
using BGCS.CppAst.Parsing;
using BGCS.Metadata;
using BGCS.Patching;
using Xunit;

namespace BGCS.Tests;

public class PatchEngineTests
{
    [Fact]
    public void AnalysisPatchesRunInOrderWithoutWritingInputFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-analysis-patch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "api.hpp");
        const string input = "class Source { };";
        File.WriteAllText(source, input);
        try
        {
            using var compilation = CppParser.ParseFile(source);
            Assert.False(compilation.hasErrors);
            PatchEngine engine = new();
            engine.RegisterPrePatch(new AppendPrePatch("First"));
            engine.RegisterPrePatch(new AppendPrePatch("Second"));
            engine.ApplyPrePatches(new CsCodeGeneratorConfig(), new ParseResult(compilation));
            Assert.Equal("SourceFirstSecond", compilation.classes[0].name);
            Assert.Equal(input, File.ReadAllText(source));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ApplyPostPatches_ShouldApplyStagesInOrderAndWriteBack()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-postpatch-" + Guid.NewGuid().ToString("N"));
        string outputRoot = Path.Combine(temp, "output");
        string stages = Path.Combine(temp, "stages");
        Directory.CreateDirectory(outputRoot);

        string relativePath = "sample.txt";
        string fullPath = Path.Combine(outputRoot, relativePath);
        File.WriteAllText(fullPath, "base");

        try
        {
            PatchEngine engine = new();
            engine.RegisterPostPatch(new AppendPostPatch(relativePath, "-A"));
            engine.RegisterPostPatch(new AppendPostPatch(relativePath, "-B"));

            engine.ApplyPostPatches(
                new CsCodeGeneratorMetadata(),
                outputRoot,
                [fullPath]);

            Assert.Equal("base-A-B", File.ReadAllText(fullPath));
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, true);
            }
        }
    }

    private sealed class AppendPrePatch : IPrePatch
    {
        private readonly string m_suffix;
        public AppendPrePatch(string suffix) => m_suffix = suffix;
        public void Apply(
            CsCodeGeneratorConfig settings,
            ParseResult result
        ) {
            result.compilation.classes[0].name += m_suffix;
        }
    }

    private sealed class AppendPostPatch : IPostPatch
    {
        private readonly string path;
        private readonly string suffix;

        public AppendPostPatch(string path, string suffix)
        {
            this.path = path;
            this.suffix = suffix;
        }

        public void Apply(PatchContext context, CsCodeGeneratorMetadata metadata, List<string> files)
        {
            string text = context.ReadFile(path);
            context.WriteFile(path, text + suffix);
        }
    }
}
