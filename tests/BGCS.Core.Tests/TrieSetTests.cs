using System;
using System.Collections.Generic;
using System.Linq;
using BGCS.Core.Collections;
using Xunit;

#pragma warning disable xUnit2017 // TrieSet.Contains validates trie-key semantics, not enumerator item equality.

namespace BGCS.Core.Tests;

public class TrieSetTests
{
    [Fact]
    public void Add_Contains_Remove_ShouldWork()
    {
        TrieSet<char> set = new();
        var key = "hello".ToCharArray();

        set.Add(key);
        Assert.True(set.Contains(key));

        var removed = set.Remove(key);
        Assert.True(removed);
        Assert.False(set.Contains(key));
    }

    [Fact]
    public void Add_DuplicateKey_ShouldThrow()
    {
        TrieSet<char> set = new();
        char[] key = "dup".ToCharArray();

        set.Add(key);

        Assert.Throws<InvalidOperationException>(() => set.Add(key));
    }

    [Fact]
    public void Remove_PrefixKey_ShouldNotRemoveLongerKey()
    {
        TrieSet<char> set = new();
        char[] shortKey = "abc".ToCharArray();
        char[] longKey = "abcd".ToCharArray();

        set.Add(shortKey);
        set.Add(longKey);

        Assert.True(set.Remove(shortKey));
        Assert.False(set.Contains(shortKey));
        Assert.True(set.Contains(longKey));
    }

    [Fact]
    public void GetByPrefix_ShouldReturnOnlyMatchingKeys()
    {
        TrieSet<char> set = new();
        set.Add("apple".ToCharArray());
        set.Add("application".ToCharArray());
        set.Add("banana".ToCharArray());

        List<string> matches = set.GetByPrefix("app".ToCharArray())
            .Select(x => new string(x.ToArray()))
            .OrderBy(x => x)
            .ToList();

        Assert.Equal(2, matches.Count);
        Assert.Contains("apple", matches);
        Assert.Contains("application", matches);
        Assert.DoesNotContain("banana", matches);
    }

    [Fact]
    public void CopyTo_And_Clear_ShouldKeepConsistentCount()
    {
        TrieSet<char> set = new();
        set.Add("one".ToCharArray());
        set.Add("two".ToCharArray());

        IEnumerable<char>[] arr = new IEnumerable<char>[2];
        set.CopyTo(arr, 0);

        string[] copied = arr.Select(x => new string(x.ToArray())).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "one", "two" }, copied);

        set.Clear();
        Assert.Empty(set);
    }

    [Fact]
    public void StoredKeys_AreOwnedAndCannotBeChangedThroughTheSourceOrEnumeration()
    {
        var set = new TrieSet<char>();
        char[] key = "abc".ToCharArray();
        set.Add(key);
        key[0] = 'z';

        var stored = Assert.IsAssignableFrom<IList<char>>(Assert.Single(set));
        Assert.Throws<NotSupportedException>(() => stored[0] = 'z');
        Assert.True(set.Contains("abc"));
        Assert.False(set.Contains("zbc"));
    }

    [Theory]
    [InlineData("ab", "", "")]
    [InlineData("zebra", "", "")]
    [InlineData("abcdef", "abcd", "abc")]
    public void PrefixMatching_AdvancesThroughNodesAndReturnsOnlyStoredKeys(
        string input,
        string longest,
        string shortest
    ) {
        var set = new TrieSet<char>();
        set.Add("abc");
        set.Add("abcd");

        Assert.Equal(longest, new string(set.FindLargestMatch(input.AsSpan())));
        Assert.Equal(shortest, new string(set.FindSmallestMatch(input.AsSpan())));
    }

    [Fact]
    public void EmptyKey_ParticipatesInEnumerationAndClear()
    {
        var set = new TrieSet<char>();
        set.Add(Array.Empty<char>());

        Assert.Empty(Assert.Single(set));
        Assert.Empty(set.FindSmallestMatch("abc".AsSpan()).ToArray());
        set.Clear();
        Assert.False(set.Contains(Array.Empty<char>()));
        set.Add(Array.Empty<char>());
        Assert.Single(set);
    }
}
