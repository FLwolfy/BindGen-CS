using System;
using System.Collections.Generic;
using System.IO;
using BGCS.Metadata;
using BGCS.Patching;
using Xunit;

namespace BGCS.Tests;

public class PatchMatrixTests
{
    [Fact]
    public void ApplyPostPatches_MultiStage_ShouldChainMutationsAndCreateFiles()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-patch-matrix-post-" + Guid.NewGuid().ToString("N"));
        string outputRoot = Path.Combine(temp, "output");
        string stages = Path.Combine(temp, "stages");
        Directory.CreateDirectory(outputRoot);

        string target = Path.Combine(outputRoot, "Functions", "Functions.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, "alpha");

        try
        {
            PatchEngine engine = new(stages);
            engine.RegisterPostPatch(new ReplacePostPatch("Functions/Functions.cs", "alpha", "beta"));
            engine.RegisterPostPatch(new AppendPostPatch("Functions/Functions.cs", "-gamma"));
            engine.RegisterPostPatch(new CreateFilePostPatch("Diagnostics/patch.log", "ok"));

            engine.ApplyPostPatches(new CsCodeGeneratorMetadata(), outputRoot, [target]);

            Assert.Equal("beta-gamma", File.ReadAllText(target));
            Assert.Equal("ok", File.ReadAllText(Path.Combine(outputRoot, "Diagnostics", "patch.log")));
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
    public void ApplyPrePatches_MultiStage_ShouldPreservePreviousStageOutputs()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-patch-matrix-pre-" + Guid.NewGuid().ToString("N"));
        string inputRoot = Path.Combine(temp, "input");
        string stages = Path.Combine(temp, "stages");
        Directory.CreateDirectory(inputRoot);

        string input = Path.Combine(inputRoot, "header.h");
        File.WriteAllText(input, "base");

        try
        {
            PatchEngine engine = new(stages);
            engine.RegisterPrePatch(new AppendPrePatch("header.h", "-p1"));
            engine.RegisterPrePatch(new AppendPrePatch("header.h", "-p2"));
            engine.RegisterPrePatch(new CreateFilePrePatch("generated/pre.txt", "pre-generated"));

            engine.ApplyPrePatches(new CsCodeGeneratorConfig(), inputRoot, [input], null!);

            Assert.Equal("base-p1-p2", File.ReadAllText(input));
            Assert.Equal("pre-generated", File.ReadAllText(Path.Combine(inputRoot, "generated", "pre.txt")));
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
    public void ApplyPostPatches_EmptyPatchList_ShouldKeepFilesUnchanged()
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-patch-matrix-empty-" + Guid.NewGuid().ToString("N"));
        string outputRoot = Path.Combine(temp, "output");
        Directory.CreateDirectory(outputRoot);

        string target = Path.Combine(outputRoot, "sample.txt");
        File.WriteAllText(target, "stable");

        try
        {
            PatchEngine engine = new(Path.Combine(temp, "stages"));
            engine.ApplyPostPatches(new CsCodeGeneratorMetadata(), outputRoot, [target]);
            Assert.Equal("stable", File.ReadAllText(target));
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

    private sealed class CreateFilePrePatch : IPrePatch
    {
        private readonly string path;
        private readonly string content;

        public CreateFilePrePatch(string path, string content)
        {
            this.path = path;
            this.content = content;
        }

        public void Apply(PatchContext context, CsCodeGeneratorConfig settings, List<string> files, ParseResult compilation)
        {
            context.WriteFile(path, content);
        }
    }

    private sealed class ReplacePostPatch : IPostPatch
    {
        private readonly string path;
        private readonly string oldValue;
        private readonly string newValue;

        public ReplacePostPatch(string path, string oldValue, string newValue)
        {
            this.path = path;
            this.oldValue = oldValue;
            this.newValue = newValue;
        }

        public void Apply(PatchContext context, CsCodeGeneratorMetadata metadata, List<string> files)
        {
            string text = context.ReadFile(path);
            context.WriteFile(path, text.Replace(oldValue, newValue, StringComparison.Ordinal));
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

    private sealed class CreateFilePostPatch : IPostPatch
    {
        private readonly string path;
        private readonly string content;

        public CreateFilePostPatch(string path, string content)
        {
            this.path = path;
            this.content = content;
        }

        public void Apply(PatchContext context, CsCodeGeneratorMetadata metadata, List<string> files)
        {
            context.WriteFile(path, content);
        }
    }
}
