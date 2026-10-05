using System;
using System.Linq;
using BGCS.Core.Collections;
using BGCS.Core.Text;
using Xunit;

#pragma warning disable xUnit2017 // TrieStringSet.Contains honors its configured key comparer.

namespace BGCS.Core.Tests;

public class TrieStringSetTests
{
    [Fact]
    public void AddRange_Contains_Remove_ShouldWork()
    {
        TrieStringSet set = new();
        set.AddRange(new[] { "alpha", "beta", "gamma" });

        Assert.Equal(3, set.Count);
        Assert.True(set.Contains("beta"));
        Assert.True(set.Remove("beta"));
        Assert.False(set.Contains("beta"));
        Assert.Equal(2, set.Count);
    }

    [Fact]
    public void Add_DuplicateKey_ShouldThrow()
    {
        TrieStringSet set = new();
        set.Add("dup");

        Assert.Throws<InvalidOperationException>(() => set.Add("dup"));
    }

    [Fact]
    public void CaseInsensitiveComparer_ShouldTreatKeysAsEqual()
    {
        TrieStringSet set = new(CharCaseInsensitiveEqualityComparer.@default);
        set.Add("Test");

        Assert.True(set.Contains("test"));
        Assert.True(set.Contains("TEST"));
    }

    [Fact]
    public void FindLargestMatch_ShouldReturnLongestStoredPrefix()
    {
        TrieStringSet set = new();
        set.Add("a");
        set.Add("abc");
        set.Add("abcd");

        var match = set.FindLargestMatch("abcdef".AsSpan());
        Assert.Equal("abcd", match.ToString());
    }

    [Fact]
    public void PrefixQueries_DoNotConfuseIntermediatePathsWithStoredKeys()
    {
        TrieStringSet set = new();
        set.Add("car");
        set.Add("cart");

        Assert.True(set.Contains("car"));
        Assert.False(set.Contains("ca"));
        Assert.Equal(new[] { "car", "cart" }, set.GetByPrefix("ca").OrderBy(x => x));
    }

    [Theory]
    [InlineData("ab", "", "")]
    [InlineData("zebra", "", "")]
    [InlineData("abcdef", "abcd", "abc")]
    [InlineData("", "", "")]
    public void PrefixMatching_RequiresACompleteStoredKey(
        string input,
        string longest,
        string shortest
    ) {
        var set = new TrieStringSet();
        set.AddRange(new[] { "abc", "abcd" });

        Assert.Equal(longest, set.FindLargestMatch(input.AsSpan()).ToString());
        Assert.Equal(shortest, set.FindSmallestMatch(input.AsSpan()).ToString());
    }

    [Fact]
    public void EmptyKey_IsEnumeratedRemovedAndClearedConsistently()
    {
        var set = new TrieStringSet();
        set.Add(string.Empty);

        Assert.Equal(string.Empty, Assert.Single(set));
        Assert.True(set.Remove(string.Empty));
        Assert.False(set.Remove(string.Empty));
        set.Add(string.Empty);
        set.Clear();
        Assert.False(set.Contains(string.Empty));
        set.Add(string.Empty);
        Assert.Single(set);
    }

    [Fact]
    public void Enumeration_And_CopyTo_ShouldContainAllValues()
    {
        TrieStringSet set = new();
        set.Add("x");
        set.Add("y");
        set.Add("z");

        string[] copied = new string[3];
        set.CopyTo(copied, 0);

        var fromCopy = copied.OrderBy(x => x).ToArray();
        var fromEnum = set.OrderBy(x => x).ToArray();

        Assert.Equal(new[] { "x", "y", "z" }, fromCopy);
        Assert.Equal(new[] { "x", "y", "z" }, fromEnum);
    }
}
