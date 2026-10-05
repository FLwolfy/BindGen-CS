using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using BGCS.Core.Collections;
using Xunit;

namespace BGCS.Core.Tests;

public sealed class DictionaryContractTests
{
    [Fact]
    public void ClonePreservesKeyComparisonAndOwnsItsContainer()
    {
        Dictionary<string, int> source = new(StringComparer.OrdinalIgnoreCase) { ["Value"] = 7 };
        Dictionary<string, int> clone = source.Clone();
        Assert.Same(source.Comparer, clone.Comparer);
        Assert.Equal(7, clone["VALUE"]);
        clone["value"] = 9;
        Assert.Equal(7, source["Value"]);
        Assert.Single(clone);
    }

    [Fact]
    public void PairRemovalRequiresMatchingValue()
    {
        TernarySearchTreeDictionary<int> dictionary = new() { ["value"] = 7 };
        ICollection<KeyValuePair<string, int>> collection = dictionary;
        Assert.False(collection.Remove(new("value", 8)));
        Assert.Equal(7, dictionary["value"]);
        Assert.True(collection.Remove(new("value", 7)));
        Assert.Empty(dictionary);
    }

    [Fact]
    public void CopyValidatesCapacityBeforeWritingAndAllowsEmptyDestination()
    {
        TernarySearchTreeDictionary<int> dictionary = new() { ["value"] = 7 };
        ICollection<KeyValuePair<string, int>> collection = dictionary;
        KeyValuePair<string, int>[] destination = [new("unchanged", 9)];
        Assert.Throws<ArgumentException>(() => collection.CopyTo(destination, 1));
        Assert.Throws<ArgumentException>(() => ((ICollection)dictionary).CopyTo(destination, 1));
        Assert.Equal("unchanged", destination[0].Key);
        collection.CopyTo(destination, 0);
        Assert.Equal("value", destination[0].Key);
        dictionary.Clear();
        collection.CopyTo([], 0);
        ((ICollection)dictionary).CopyTo(Array.Empty<KeyValuePair<string, int>>(), 0);
    }

    [Fact]
    public void OrdinalIgnoreCaseIsIndependentOfCurrentCulture()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            TernarySearchTreeDictionary<int> dictionary = new(StringComparer.OrdinalIgnoreCase) { ["I"] = 7 };
            Assert.Equal(7, dictionary["i"]);
            Assert.Throws<ArgumentException>(() => dictionary.Add("i", 8));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("a", "ab", "ac")]
    [InlineData("ab", "a", "abc")]
    public void ExactLookupNeverResolvesAStoredPrefixOrAnUnstoredBranch(
        string stored,
        string absent,
        string another
    ) {
        TernarySearchTreeDictionary<int> dictionary = new() { [stored] = 7 };
        Assert.False(dictionary.ContainsKey(absent));
        Assert.False(dictionary.TryGetValue(absent, out _));
        dictionary[another] = 9;
        Assert.False(dictionary.ContainsKey(absent));
        Assert.False(dictionary.Remove(absent));
        Assert.Equal(7, dictionary[stored]);
        Assert.Equal(9, dictionary[another]);
    }

    [Fact]
    public void ResetInvalidatesThePreviouslyCurrentEntry()
    {
        TernarySearchTreeDictionary<int> dictionary = new() { ["a"] = 7, ["ab"] = 9 };
        using IEnumerator<KeyValuePair<string, int>> enumerator = dictionary.GetEnumerator();
        Assert.True(enumerator.MoveNext());
        enumerator.Reset();
        Assert.Throws<InvalidOperationException>(() => enumerator.Current);
        Assert.True(enumerator.MoveNext());
        Assert.Contains(enumerator.Current.Key, new[] { "a", "ab" });
    }
}
