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
            PatchEngine engine = new();
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
    public void FailingPostPatchPreservesTheCompletePreviousTree()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-patch-rollback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string target = Path.Combine(root, "Bindings.cs");
        string unselected = Path.Combine(root, "notes.txt");
        File.WriteAllText(target, "original");
        File.WriteAllText(unselected, "retained");
        try
        {
            PatchEngine engine = new();
            engine.RegisterPostPatch(new AppendPostPatch("Bindings.cs", "-candidate"));
            engine.RegisterPostPatch(new ThrowingPostPatch());
            Assert.Throws<InvalidOperationException>(() => engine.ApplyPostPatches(new CsCodeGeneratorMetadata(), root, [target]));
            Assert.Equal("original", File.ReadAllText(target));
            Assert.Equal("retained", File.ReadAllText(unselected));
            PatchEngine success = new();
            success.RegisterPostPatch(new AppendPostPatch("Bindings.cs", "-complete"));
            success.ApplyPostPatches(new CsCodeGeneratorMetadata(), root, [target]);
            Assert.Equal("original-complete", File.ReadAllText(target));
            Assert.Equal("retained", File.ReadAllText(unselected));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
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
            PatchEngine engine = new();
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

    private sealed class ThrowingPostPatch : IPostPatch
    {
        public void Apply(
            PatchContext context,
            CsCodeGeneratorMetadata metadata,
            List<string> files
        ) => throw new InvalidOperationException("Candidate transformation failed.");
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
