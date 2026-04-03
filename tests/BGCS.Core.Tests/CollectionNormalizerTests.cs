using System.Collections.Generic;
using BGCS.Core.Collections;
using Xunit;

namespace BGCS.Core.Tests;

public class CollectionNormalizerTests
{
    private sealed class Dummy
    {
        public string Name { get; set; } = string.Empty;
        public List<int>? Items { get; set; }
        public HashSet<string>? Tags { get; set; }
        public Dictionary<string, int>? Mapping { get; set; }
        public IEnumerable<int>? EnumerableOnly { get; set; }
    }

    [Fact]
    public void Normalize_ShouldInstantiateCommonGenericCollections()
    {
        Dummy obj = new();

        CollectionNormalizer.Normalize(obj);

        Assert.NotNull(obj.Items);
        Assert.NotNull(obj.Tags);
        Assert.NotNull(obj.Mapping);
    }

    [Fact]
    public void Normalize_ShouldNotModifyAlreadyInitializedCollections()
    {
        Dummy obj = new()
        {
            Items = new() { 1, 2, 3 },
            Tags = new() { "x" },
            Mapping = new() { ["a"] = 1 }
        };

        CollectionNormalizer.Normalize(obj);

        Assert.Equal(3, obj.Items!.Count);
        Assert.Single(obj.Tags!);
        Assert.Equal(1, obj.Mapping!["a"]);
    }
}
