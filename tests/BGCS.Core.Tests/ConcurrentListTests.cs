using System.Collections;
using System.Collections.Generic;
using BGCS.Core.Collections;
using Xunit;

namespace BGCS.Core.Tests;

public sealed class ConcurrentListTests
{
    [Fact]
    public void EnumerationOwnsASnapshotAcrossSubsequentMutations()
    {
        var list = new ConcurrentList<int>([1, 2]);
        using IEnumerator<int> snapshot = list.GetEnumerator();
        IEnumerator untyped = ((IEnumerable)list).GetEnumerator();
        list.Clear();
        list.Add(3);

        Assert.True(snapshot.MoveNext());
        Assert.Equal(1, snapshot.Current);
        Assert.True(snapshot.MoveNext());
        Assert.Equal(2, snapshot.Current);
        Assert.False(snapshot.MoveNext());
        Assert.True(untyped.MoveNext());
        Assert.Equal(1, untyped.Current);
        Assert.Single(list);
        Assert.Equal(3, list[0]);
    }
}
