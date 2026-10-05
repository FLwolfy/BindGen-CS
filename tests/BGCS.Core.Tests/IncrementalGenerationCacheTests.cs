using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using BGCS.Core.Caching;
using Xunit;

namespace BGCS.Core.Tests;

public sealed class IncrementalGenerationCacheTests
{
    [Fact]
    public void StoreRequiresCurrentOutputEvenWhenTheEntryIsAlreadyComplete()
    {
        using TestDirectory directory = new();
        string input = directory.Write("api.h", "int sample(void);");
        string output = directory.Create("Generated");
        directory.Write("Generated/Bindings.cs", "complete output");
        var cache = new IncrementalGenerationCache(directory.PathOf("cache"));
        IncrementalCacheKey key = IncrementalGenerationCache.CreateKey("generator", [input]);
        cache.Store(key, output, "metadata");
        Directory.Delete(output, recursive: true);

        Assert.Throws<DirectoryNotFoundException>(() => cache.Store(key, output, "metadata"));
        Assert.True(cache.TryRestore(key, output, out _));
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("metadata")]
    [InlineData("malformed")]
    public void DamagedCacheIsAMissPreservesOutputAndCanBeReplaced(string damage)
    {
        using TestDirectory directory = new();
        string input = directory.Write("api.h", "int sample(void);");
        string output = directory.Create("Generated");
        directory.Write("Generated/Bindings.cs", "complete output");
        var cache = new IncrementalGenerationCache(directory.PathOf("cache"));
        IncrementalCacheKey key = IncrementalGenerationCache.CreateKey("generator", [input]);
        cache.Store(key, output, "complete metadata");
        string entry = directory.PathOf("cache/" + key.value);
        string marker = Path.Combine(entry, "entry.json");
        string cachedSource = Path.Combine(entry, "files", "Bindings.cs");
        switch (damage)
        {
            case "changed": File.WriteAllText(cachedSource, "damaged output"); break;
            case "missing": File.Delete(cachedSource); break;
            case "extra": File.WriteAllText(Path.Combine(entry, "files", "Extra.cs"), "unexpected output"); break;
            case "metadata":
                JsonNode descriptor = JsonNode.Parse(File.ReadAllText(marker))!;
                descriptor["metadata"] = "changed metadata";
                File.WriteAllText(marker, descriptor.ToJsonString());
                break;
            case "malformed": File.WriteAllText(marker, "invalid json"); break;
            default: throw new ArgumentException("Unknown test damage.", nameof(damage));
        }

        Assert.False(cache.TryRestore(key, output, out string unavailable));
        Assert.Equal(string.Empty, unavailable);
        Assert.Equal("complete output", File.ReadAllText(Path.Combine(output, "Bindings.cs")));
        cache.Store(key, output, "complete metadata");
        Assert.True(cache.TryRestore(key, output, out string metadata));
        Assert.Equal("complete metadata", metadata);
        Assert.Equal("complete output", File.ReadAllText(cachedSource));
    }

    [Fact]
    public async Task OpposingStoreAndRestoreAcquireDirectoryOwnershipInTheSameOrder()
    {
        using TestDirectory directory = new();
        string input = directory.Write("api.h", "int sample(void);");
        string output = directory.Create("Generated");
        directory.Write("Generated/Bindings.cs", "complete output");
        var cache = new IncrementalGenerationCache(directory.PathOf("cache"));
        IncrementalCacheKey key = IncrementalGenerationCache.CreateKey("generator", [input]);
        cache.Store(key, output, "metadata");
        await Task.WhenAll(Enumerable.Range(0, 8).Select(index => Task.Run(() =>
        {
            if (index % 2 == 0)
                cache.Store(key, output, "metadata");
            else
                Assert.True(cache.TryRestore(key, output, out _));
        }))).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal("complete output", File.ReadAllText(Path.Combine(output, "Bindings.cs")));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("short")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void CacheKeysCannotSelectArbitraryDirectories(string value)
        => Assert.Throws<ArgumentException>(() => new IncrementalCacheKey(value, 1));

    [Fact]
    public void OutputCannotOverlapItsCacheTree()
    {
        using TestDirectory directory = new();
        string input = directory.Write("api.h", "int sample(void);");
        string root = directory.Create("cache");
        var cache = new IncrementalGenerationCache(root);
        IncrementalCacheKey key = IncrementalGenerationCache.CreateKey("generator", [input]);
        foreach (string output in new[] { root, Path.Combine(root, "Generated"), directory.PathOf(".") })
        {
            Assert.Throws<ArgumentException>(() => cache.Store(key, output, "metadata"));
            Assert.Throws<ArgumentException>(() => cache.TryRestore(key, output, out _));
        }
    }

    [Fact]
    public void InputLengthsSeparateFileContentsFromTheFollowingPath()
    {
        using TestDirectory directory = new();
        string first = directory.Write("first.h", "prefix");
        string second = directory.Write("second.h", "");
        byte[] path = Encoding.UTF8.GetBytes(Path.GetFullPath(second).Replace('\\', '/'));
        byte[] delimiter = [.. BitConverter.GetBytes(path.Length), .. path];
        File.WriteAllBytes(second, [.. delimiter, .. Encoding.UTF8.GetBytes("end")]);
        IncrementalCacheKey before = IncrementalGenerationCache.CreateKey("generator", [first, second]);
        File.WriteAllBytes(first, [.. Encoding.UTF8.GetBytes("prefix"), .. delimiter]);
        File.WriteAllText(second, "end");
        IncrementalCacheKey after = IncrementalGenerationCache.CreateKey("generator", [first, second]);
        Assert.NotEqual(before.value, after.value);
    }

    [Fact]
    public void CppSdkHeadersWithoutExtensionsParticipateInInputIdentity()
    {
        using TestDirectory directory = new();
        string header = directory.Write("include/vector", "namespace std { struct vector; }\n");
        directory.Write("include/module.lib", "binary");
        var files = IncrementalGenerationCache.DiscoverInputs([], [directory.PathOf("include")]);
        Assert.Equal(Path.GetFullPath(header), Assert.Single(files));
        IncrementalCacheKey first = IncrementalGenerationCache.CreateKey("cpp-sdk", files);
        File.WriteAllText(header, "namespace std { template<class T> struct vector; }\n");
        Assert.NotEqual(first.value, IncrementalGenerationCache.CreateKey("cpp-sdk", files).value);
    }

    [Fact]
    public void Cache_RestoresCompleteOutputAndInvalidatesOnInputContentChange()
    {
        using TestDirectory directory = new();
        string input = directory.Write("input/sample.h", "int sample(void);\n");
        string output = directory.Create("Generated");
        directory.Write("Generated/Bindings.cs", "// v1\n");
        IncrementalGenerationCache cache = new(directory.PathOf("cache"));
        IncrementalCacheKey first = IncrementalGenerationCache.CreateKey("generator-v1", [input]);
        cache.Store(first, output, "metadata-v1");
        File.WriteAllText(Path.Combine(output, "Bindings.cs"), "corrupted\n");

        Assert.True(cache.TryRestore(first, output, out string metadata));
        Assert.Equal("metadata-v1", metadata);
        Assert.Equal("// v1\n", File.ReadAllText(Path.Combine(output, "Bindings.cs")));

        File.WriteAllText(input, "int changed(void);\n");
        IncrementalCacheKey second = IncrementalGenerationCache.CreateKey("generator-v1", [input]);
        Assert.NotEqual(first.value, second.value);
        Assert.False(cache.TryRestore(second, output, out _));
    }

    [Fact]
    public async Task Cache_ConcurrentPublicationProducesOneValidImmutableEntry()
    {
        using TestDirectory directory = new();
        string input = directory.Write("input.h", "int value;\n");
        string output = directory.Create("Generated");
        directory.Write("Generated/Bindings.cs", "// deterministic\n");
        IncrementalGenerationCache cache = new(directory.PathOf("cache"));
        IncrementalCacheKey key = IncrementalGenerationCache.CreateKey("generator", [input]);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => cache.Store(key, output, "state"))));

        string restored = directory.PathOf("Restored");
        Assert.True(cache.TryRestore(key, restored, out string metadata));
        Assert.Equal("state", metadata);
        Assert.Equal("// deterministic\n", File.ReadAllText(Path.Combine(restored, "Bindings.cs")));
        Assert.Single(Directory.GetDirectories(directory.PathOf("cache")));
    }

    private sealed class TestDirectory : IDisposable
    {
        public TestDirectory()
        {
            Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bgcs-cache-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        private string Root { get; }
        public string PathOf(string relative) => System.IO.Path.Combine(Root, relative);
        public string Create(string relative) { string path = PathOf(relative); Directory.CreateDirectory(path); return path; }
        public string Write(string relative, string content)
        {
            string path = PathOf(relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
}
