using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using BGCS.Metadata;
using BGCS.Patching;
using Xunit;

namespace BGCS.Tests;

public sealed class PatchOwnershipTests
{
    [Fact]
    public void MatchOffsetsRemainCorrectWhenReplacementsChangeLength()
    {
        string text = "item item item";
        new ExpandMatchPatch().PostPatch("Bindings.cs", ref text);
        Assert.Equal("expanded-item expanded-item expanded-item", text);
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData(".")]
    [InlineData("sub/../../outside.txt")]
    public void PatchFilesCannotEscapeTheCandidate(string path)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-owned-patch-" + Guid.NewGuid().ToString("N"));
        PatchContext context = new(root);
        Assert.Throws<ArgumentException>(() => context.WriteFile(path, "invalid"));
        Assert.False(Directory.Exists(root));
        Assert.Throws<ArgumentException>(() => context.GetFullPath(Path.GetFullPath(root)));
    }

    [Fact]
    public void LaterStagesReceiveFilesCreatedByEarlierStages()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-created-patch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            PatchEngine engine = new();
            engine.RegisterPostPatch(new CreateCandidatePatch());
            engine.RegisterPostPatch(new TransformNewFilesPatch());
            engine.ApplyPostPatches(new CsCodeGeneratorMetadata(), root, []);
            Assert.Equal("created-transformed", File.ReadAllText(Path.Combine(root, "Generated", "new.cs")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class ExpandMatchPatch : RegexPatch
    {
        public ExpandMatchPatch() : base("item")
        {
        }

        protected override void PostPatchMatch(
            string file,
            ref string text,
            Match match
        ) {
            text = text.Remove(match.Index, match.Length).Insert(match.Index, "expanded-item");
        }
    }

    private sealed class CreateCandidatePatch : IPostPatch
    {
        public void Apply(
            PatchContext context,
            CsCodeGeneratorMetadata metadata,
            List<string> files
        ) => context.WriteFile("Generated/new.cs", "created");
    }

    private sealed class TransformNewFilesPatch : IPostPatch
    {
        public void Apply(
            PatchContext context,
            CsCodeGeneratorMetadata metadata,
            List<string> files
        ) {
            string file = Assert.Single(files);
            context.WriteFile(file, context.ReadFile(file) + "-transformed");
        }
    }
}
