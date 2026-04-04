using BGCS.Core;
using Xunit;

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
