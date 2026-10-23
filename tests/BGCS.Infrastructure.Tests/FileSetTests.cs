using BGCS.Core.Hosting;
using System;
using System.Collections.Generic;
using System.IO;
using BGCS.Infrastructure.IO;
using Xunit;

#pragma warning disable xUnit2017 // FileSet has path normalization semantics that collection assertions bypass.

namespace BGCS.Infrastructure.Tests;

public class FileSetTests
{
    private readonly IHostPathService m_paths = HostTestPaths.Create();
    [Fact]
    public void Construction_CapturesNormalizedPathsWithoutRetainingMutableInputs()
    {
        string[] paths = ["sample.h", Path.Combine("child", "..", "sample.h")];
        FileSet files = new(paths, m_paths);
        paths[0] = "replacement.h";

        Assert.Single(files);
        Assert.True(files.Contains(Path.GetFullPath("sample.h")));
        Assert.False(files.Contains("replacement.h"));
        Assert.False((object)files is ISet<string>);
        Assert.Equal([Path.GetFullPath("sample.h")], files);
    }

    [Fact]
    public void SetRelations_NormalizeEveryComparedCollection()
    {
        FileSet files = new(["a.h", "b.h"], m_paths);
        string relativeA = Path.Combine("child", "..", "a.h");

        Assert.True(files.SetEquals([relativeA, Path.GetFullPath("b.h"), "a.h"]));
        Assert.True(files.IsProperSupersetOf([relativeA]));
        Assert.True(files.IsSupersetOf([relativeA, "b.h"]));
        Assert.True(files.IsProperSubsetOf([relativeA, "b.h", "c.h"]));
        Assert.True(files.IsSubsetOf([relativeA, "b.h"]));
        Assert.True(files.Overlaps([relativeA, "c.h"]));
        Assert.False(files.Overlaps(["c.h"]));
        Assert.False(files.SetEquals(["a.h"]));
    }

    [Fact]
    public void Membership_UsesTheHostFileSystemComparison()
    {
        FileSet files = new(["CaseSensitive.h"], m_paths);

        Assert.Equal(!m_paths.GetSemantics(Directory.GetCurrentDirectory()).caseSensitive, files.Contains("casesensitive.h"));
    }

    [Fact]
    public void Construction_RejectsInvalidEntriesAndNullCollections()
    {
        Assert.Throws<ArgumentNullException>(() => new FileSet(null!, m_paths));
        Assert.Throws<ArgumentException>(() => new FileSet([" "], m_paths));
        Assert.Throws<ArgumentException>(() => new FileSet(["invalid\0path"], m_paths));
        FileSet files = new(["a.h"], m_paths);
        Assert.Throws<ArgumentNullException>(() => files.SetEquals(null!));
    }

    [Fact]
    public void Contains_WhenPathIsEmpty_ShouldReturnFalse()
    {
        FileSet files = new(["a.h", "b.h"], m_paths);

        Assert.False(files.Contains(string.Empty));
        Assert.False(files.Contains(null!));
    }
}
