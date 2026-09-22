using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BGCS.Core.Caching;
using Xunit;

namespace BGCS.Core.Tests;

public sealed class IncrementalGenerationCacheTests
{
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
        Assert.NotEqual(first.Value, second.Value);
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
