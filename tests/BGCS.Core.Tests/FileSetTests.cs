using BGCS.Core;
using Xunit;

#pragma warning disable xUnit2017 // FileSet has path normalization semantics that collection assertions bypass.

namespace BGCS.Core.Tests;

public class FileSetTests
{
    [Fact]
    public void Contains_WhenPathIsEmpty_ShouldReturnFalse()
    {
        FileSet files = new(["a.h", "b.h"]);

        Assert.False(files.Contains(string.Empty));
        Assert.False(files.Contains(null!));
    }
}
