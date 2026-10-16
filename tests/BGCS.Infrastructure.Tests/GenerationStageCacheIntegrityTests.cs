using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Common;
using BGCS.Infrastructure.Caching;
using Xunit;

namespace BGCS.Infrastructure.Tests;

public sealed class GenerationStageCacheIntegrityTests
{
    [Theory]
    [InlineData("../escape")]
    [InlineData("short")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void InvalidKeysCannotSelectCacheFilesystemPaths(string value)
    {
        using Scratch scratch = new();
        GenerationStageCache cache = new(scratch.root);
        Assert.Throws<ArgumentException>(() => cache.Store(new ContentDigest(value), Set("must not write")));
        Assert.False(Directory.Exists(scratch.root));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("reordered")]
    [InlineData("duplicate")]
    [InlineData("truncated")]
    [InlineData("member-digest")]
    [InlineData("media-type")]
    [InlineData("path")]
    [InlineData("missing-seal")]
    public void CompleteSetTamperingIsAMissAndFreshCandidatesRepairEveryByte(string damage)
    {
        using Scratch scratch = new();
        GenerationStageCache cache = new(scratch.root);
        ContentDigest key = Digest("complete-key");
        ImmutableArray<CachedStageArtifact> candidates = Set("original");
        Assert.True(cache.Store(key, candidates));
        string path = Assert.Single(Directory.GetFiles(scratch.root, "*.stage.json"));
        JsonObject entry = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        JsonArray artifacts = entry["artifacts"]!.AsArray();
        switch (damage)
        {
            case "missing": artifacts.RemoveAt(0); break;
            case "extra":
                JsonObject additional = artifacts[0]!.DeepClone().AsObject();
                additional["path"] = "Unexpected.g.cs";
                artifacts.Add(additional);
                break;
            case "reordered":
                JsonNode first = artifacts[0]!.DeepClone();
                artifacts.RemoveAt(0);
                artifacts.Add(first);
                break;
            case "duplicate": artifacts.Add(artifacts[0]!.DeepClone()); break;
            case "truncated": artifacts[0]!["content"] = Convert.ToBase64String("cut"u8); break;
            case "member-digest": artifacts[0]!["digest"] = Digest("other bytes").value; break;
            case "media-type": artifacts[0]!["mediaType"] = "text/plain"; break;
            case "path": artifacts[0]!["path"] = "Renamed.g.cs"; break;
            case "missing-seal": entry.Remove("seal"); break;
            default: throw new ArgumentOutOfRangeException(nameof(damage));
        }
        File.WriteAllText(path, entry.ToJsonString());
        Assert.False(cache.TryRead(key, out var unavailable, out string? reason));
        Assert.Empty(unavailable);
        Assert.NotNull(reason);
        Assert.True(cache.Store(key, candidates));
        Assert.True(cache.TryRead(key, out var repaired, out reason));
        Assert.Null(reason);
        Assert.Equal(candidates.Select(candidate => candidate.descriptor.relativePath), repaired.Select(candidate => candidate.descriptor.relativePath));
        foreach (CachedStageArtifact candidate in candidates)
        {
            CachedStageArtifact restored = Assert.Single(repaired, item => item.descriptor == candidate.descriptor);
            Assert.Equal(candidate.content.ToArray(), restored.content.ToArray());
            Assert.Equal(restored.descriptor.digest, Digest(restored.content.Span));
        }
    }

    [Fact]
    public async Task ConcurrentStoresAndReadsNeverExposeMixedOrPartialArtifactSets()
    {
        using Scratch scratch = new();
        GenerationStageCache cache = new(scratch.root);
        ContentDigest key = Digest("shared-key");
        ImmutableArray<CachedStageArtifact> first = Set("first"), second = Set("second");
        await Task.WhenAll(Enumerable.Range(0, 4).Select(index => Task.Run(() =>
        {
            for (int iteration = 0; iteration < 12; iteration++)
            {
                Assert.True(cache.Store(key, index % 2 == 0 ? first : second));
                Assert.True(cache.TryRead(key, out var captured, out string? reason), reason);
                Assert.Equal(2, captured.Length);
                string[] values = captured.Select(artifact => Encoding.UTF8.GetString(artifact.content.Span)).ToArray();
                Assert.True(values.All(value => value == "first") || values.All(value => value == "second"));
            }
        }))).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Single(Directory.GetFiles(scratch.root, "*.stage.json"));
        Assert.Empty(Directory.GetFiles(scratch.root, "*.tmp"));
    }

    [Fact]
    public void CancelledReadsAndStoresLeaveThePreviousCompleteEntryUntouched()
    {
        using Scratch scratch = new();
        GenerationStageCache cache = new(scratch.root);
        ContentDigest key = Digest("cancelled-key");
        Assert.True(cache.Store(key, Set("original")));
        string path = Assert.Single(Directory.GetFiles(scratch.root, "*.stage.json"));
        byte[] original = File.ReadAllBytes(path);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => cache.Store(key, Set("cancelled"), cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => cache.TryRead(key, out _, out _, cancellation.Token));
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.True(cache.TryRead(key, out var captured, out _));
        Assert.All(captured, artifact => Assert.Equal("original", Encoding.UTF8.GetString(artifact.content.Span)));
        Assert.Empty(Directory.GetFiles(scratch.root, "*.tmp"));
    }

    [Fact]
    public void AnUnreadableEntryIsAMissWithNoPartialArtifactsAndTheOriginalBytesRecover()
    {
        using Scratch scratch = new();
        GenerationStageCache cache = new(scratch.root);
        ContentDigest key = Digest("busy-entry");
        Assert.True(cache.Store(key, Set("original")));
        string path = Assert.Single(Directory.GetFiles(scratch.root, "*.stage.json"));
        using (FileStream blocked = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.False(cache.TryRead(key, out var captured, out string? reason));
            Assert.Empty(captured);
            Assert.NotNull(reason);
        }
        Assert.True(cache.TryRead(key, out var restored, out _));
        Assert.Equal(2, restored.Length);
        Assert.All(restored, artifact => Assert.Equal("original", Encoding.UTF8.GetString(artifact.content.Span)));
    }

    [Fact]
    public void CancellingContendedOwnershipNeverReplacesTheCompleteEntry()
    {
        using Scratch scratch = new();
        GenerationStageCache cache = new(scratch.root);
        ContentDigest key = Digest("contended-entry");
        Assert.True(cache.Store(key, Set("original")));
        string path = Assert.Single(Directory.GetFiles(scratch.root, "*.stage.json"));
        byte[] original = File.ReadAllBytes(path);
        using (FileStream owner = new(Path.Combine(scratch.root, ".entries.bgcs-lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(100));
            Assert.Throws<OperationCanceledException>(() => cache.Store(key, Set("cancelled"), cancellation.Token));
        }
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.True(cache.TryRead(key, out _, out _));
        Assert.Empty(Directory.GetFiles(scratch.root, "*.tmp"));
    }

    private static ImmutableArray<CachedStageArtifact> Set(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        return [new(new("Value.g.cs", "text/x-csharp", Digest(bytes)), bytes), new(new("Model.json", "application/json", Digest(bytes)), bytes)];
    }
    private static ContentDigest Digest(string value) => Digest(Encoding.UTF8.GetBytes(value));
    private static ContentDigest Digest(ReadOnlySpan<byte> bytes) => new(Convert.ToHexStringLower(SHA256.HashData(bytes)));
    private sealed class Scratch : IDisposable
    {
        internal string root { get; } = Path.Combine(Path.GetTempPath(), "bgcs-stage-integrity-" + Guid.NewGuid().ToString("N"));
        public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
