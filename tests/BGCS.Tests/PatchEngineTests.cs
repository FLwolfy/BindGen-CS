using System;
using System.Collections.Generic;
using System.IO;
using BGCS.Metadata;
using BGCS.Patching;
using Xunit;

namespace BGCS.Tests;

public class PatchEngineTests
{
    [Fact]
    public void ApplyPrePatches_ShouldApplyStagesInOrderAndWriteBack()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-prepatch-" + Guid.NewGuid().ToString("N"));
        string inputRoot = Path.Combine(temp, "input");
        string stages = Path.Combine(temp, "stages");
        Directory.CreateDirectory(inputRoot);

        string relativePath = "sample.txt";
        string fullPath = Path.Combine(inputRoot, relativePath);
        File.WriteAllText(fullPath, "base");

        try
        {
            PatchEngine engine = new(stages);
            engine.RegisterPrePatch(new AppendPrePatch(relativePath, "-A"));
            engine.RegisterPrePatch(new AppendPrePatch(relativePath, "-B"));

            engine.ApplyPrePatches(
                new CsCodeGeneratorConfig(),
                inputRoot,
                [fullPath],
                null!);

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
            PatchEngine engine = new(stages);
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
        private readonly string path;
        private readonly string suffix;

        public AppendPrePatch(string path, string suffix)
        {
            this.path = path;
            this.suffix = suffix;
        }

        public void Apply(PatchContext context, CsCodeGeneratorConfig settings, List<string> files, ParseResult compilation)
        {
            string text = context.ReadFile(path);
            context.WriteFile(path, text + suffix);
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
