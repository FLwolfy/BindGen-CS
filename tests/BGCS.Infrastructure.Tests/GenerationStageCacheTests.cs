using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using BGCS.Contracts.Common;
using BGCS.Infrastructure.Caching;
using Xunit;

namespace BGCS.Infrastructure.Tests;

public sealed class GenerationStageCacheTests
{
    [Fact]
    public void CompleteSetsAreCopiedVerifiedAndMalformedEntriesAreMisses()
    {
        using Scratch scratch = new();
        GenerationStageCache cache = new(scratch.root);
        ContentDigest key = Digest("generation");
        byte[] bytes = Encoding.UTF8.GetBytes("complete source");
        CachedStageArtifact source = new(new("Types/Value.g.cs", "text/x-csharp", Digest(bytes)), bytes);
        bytes[0] = 0;
        Assert.True(cache.Store(key, [source]));
        Assert.True(cache.TryRead(key, out var artifacts, out string? reason));
        Assert.Null(reason);
        Assert.Equal("complete source", Encoding.UTF8.GetString(Assert.Single(artifacts).content.Span));
        string entry = Assert.Single(Directory.GetFiles(scratch.root, "*.stage.json"));
        JsonObject document = JsonNode.Parse(File.ReadAllText(entry))!.AsObject();
        document["artifacts"]![0]!["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes("changed source"));
        File.WriteAllText(entry, document.ToJsonString());
        Assert.False(cache.TryRead(key, out artifacts, out reason));
        Assert.Empty(artifacts);
        Assert.Contains("digest", reason);
        Assert.True(cache.Store(key, [source]));
        Assert.True(cache.TryRead(key, out artifacts, out reason));
    }

    [Theory]
    [InlineData("../outside.cs")]
    [InlineData("/outside.cs")]
    [InlineData("Types\\outside.cs")]
    [InlineData("Types/./outside.cs")]
    [InlineData("Types/name:outside.cs")]
    public void CandidatePathEscapesAreRejectedBeforeAnyEntryIsInstalled(string path)
    {
        using Scratch scratch = new();
        GenerationStageCache cache = new(scratch.root);
        Assert.Throws<InvalidDataException>(() => cache.Store(Digest("escape"), [new(new(path, "text/x-csharp", Digest("value")), Encoding.UTF8.GetBytes("value"))]));
        Assert.Empty(Directory.GetFiles(scratch.root));
    }

    [Fact]
    public void MissingNullAndIncompleteEntryDescriptorsNeverReturnPartialArtifacts()
    {
        using Scratch scratch = new();
        GenerationStageCache cache = new(scratch.root);
        ContentDigest key = Digest("entry");
        Assert.False(cache.TryRead(key, out var artifacts, out var reason));
        Assert.Null(reason);
        string entry = Path.Combine(scratch.root, key.value + ".stage.json");
        foreach (string content in new[] { "broken", "null", "{\"revision\":1,\"key\":\"" + key.value + "\",\"artifacts\":[null]}" })
        {
            File.WriteAllText(entry, content);
            Assert.False(cache.TryRead(key, out artifacts, out reason));
            Assert.Empty(artifacts);
            Assert.NotNull(reason);
        }
    }

    [Fact]
    public void EntryAndTotalBudgetsBoundDiskAndPruneOnlyOwnedDigestEntries()
    {
        using Scratch scratch = new();
        GenerationStageCache cache = new(scratch.root, maximumBytes: 1024, maximumEntryBytes: 1024);
        byte[] bytes = new byte[400];
        CachedStageArtifact artifact = new(new("Value.g.cs", "text/x-csharp", Digest(bytes)), bytes);
        ContentDigest first = Digest("first");
        Assert.True(cache.Store(first, [artifact]));
        File.SetLastWriteTimeUtc(Path.Combine(scratch.root, first.value + ".stage.json"), DateTime.UtcNow.AddMinutes(-1));
        File.WriteAllText(Path.Combine(scratch.root, "user.stage.json"), "unrelated");
        Assert.True(cache.Store(Digest("second"), [artifact]));
        Assert.False(cache.TryRead(first, out _, out _));
        Assert.Equal("unrelated", File.ReadAllText(Path.Combine(scratch.root, "user.stage.json")));
        Assert.True(Directory.GetFiles(scratch.root, "*.stage.json").Where(path => Path.GetFileName(path).Length == 75).Sum(path => new FileInfo(path).Length) <= 1024);
        byte[] large = new byte[1200];
        Assert.False(cache.Store(Digest("large"), [new(new("Large.g.cs", "text/x-csharp", Digest(large)), large)]));
        Assert.Empty(Directory.GetFiles(scratch.root, "*.tmp"));
    }

    private static ContentDigest Digest(string text) => Digest(Encoding.UTF8.GetBytes(text));
    private static ContentDigest Digest(byte[] bytes) => new(Convert.ToHexStringLower(SHA256.HashData(bytes)));
    private sealed class Scratch : IDisposable
    {
        internal string root { get; } = Path.Combine(Path.GetTempPath(), "bgcs-stage-cache-" + Guid.NewGuid().ToString("N"));
        internal Scratch() => Directory.CreateDirectory(root);
        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
