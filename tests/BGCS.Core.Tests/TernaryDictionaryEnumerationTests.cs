using System.Collections.Generic;
using BGCS.Core.Collections;
using Xunit;

namespace BGCS.Core.Tests;

public sealed class TernaryDictionaryEnumerationTests
{
    [Fact]
    public void MoveNext_AfterCompletion_RemainsFalse()
    {
        TernarySearchTreeDictionary<int> dictionary = new() { ["value"] = 7 };
        using IEnumerator<KeyValuePair<string, int>> enumerator = dictionary.GetEnumerator();
        Assert.True(enumerator.MoveNext());
        Assert.Equal(7, enumerator.Current.Value);
        Assert.False(enumerator.MoveNext());
        Assert.False(enumerator.MoveNext());
    }

    [Fact]
    public void MoveNext_EmptyDictionary_RemainsFalse()
    {
        TernarySearchTreeDictionary<int> dictionary = new();
        using IEnumerator<KeyValuePair<string, int>> enumerator = dictionary.GetEnumerator();
        Assert.False(enumerator.MoveNext());
        Assert.False(enumerator.MoveNext());
    }
}
