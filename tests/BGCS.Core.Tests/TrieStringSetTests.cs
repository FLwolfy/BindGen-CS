using System;
using System.Linq;
using BGCS.Core;
using Xunit;

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
        TrieStringSet set = new(CharCaseInsensitiveEqualityComparer.Default);
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
    public void TryGetNode_ForTerminalAndIntermediateNode_ShouldBehaveAsExpected()
    {
        TrieStringSet set = new();
        set.Add("car");
        set.Add("cart");

        Assert.True(set.TryGetNode("car", out var terminalNode));
        Assert.NotNull(terminalNode);

        Assert.False(set.TryGetNode("ca", out var intermediateNode));
        Assert.NotNull(intermediateNode);
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
