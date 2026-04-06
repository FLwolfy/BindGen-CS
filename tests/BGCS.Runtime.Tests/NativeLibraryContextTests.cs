using System;
using BGCS.Runtime;
using Xunit;

namespace BGCS.Runtime.Tests;

public class NativeLibraryContextTests
{
    [Fact]
    public void Dispose_WithZeroHandle_ShouldBeIdempotent()
    {
        NativeLibraryContext context = new((nint)0);

        Exception? first = Record.Exception(context.Dispose);
        Exception? second = Record.Exception(context.Dispose);

        Assert.Null(first);
        Assert.Null(second);
    }

    [Fact]
    public void IsExtensionSupported_ShouldAlwaysReturnFalse()
    {
        NativeLibraryContext context = new((nint)0);
        try
        {
            Assert.False(context.IsExtensionSupported("GL_ARB_debug_output"));
        }
        finally
        {
            context.Dispose();
        }
    }
}
