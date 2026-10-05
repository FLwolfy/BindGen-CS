using System;
using System.Threading.Tasks;
using Xunit;

namespace BGCS.Runtime.Tests;

public class NativeLibraryContextTests
{
    [Fact]
    public void EmptyContext_RejectsLookupAfterDisposal()
    {
        using var context = new NativeLibraryContext((nint)0);
        Assert.False(context.TryGetProcAddress("missing", out nint address));
        Assert.Equal(0, address);
        context.Dispose();
        Assert.Throws<ObjectDisposedException>(() => context.GetProcAddress("missing"));
        Assert.Throws<ObjectDisposedException>(() => context.TryGetProcAddress("missing", out _));
    }

    [Fact]
    public void ConcurrentDisposal_ReleasesTheOwnedSystemModuleOnce()
    {
        string library = OperatingSystem.IsWindows() ? "kernel32.dll"
            : OperatingSystem.IsMacOS() ? "/usr/lib/libSystem.B.dylib" : "libc.so.6";
        string symbol = OperatingSystem.IsWindows() ? "GetCurrentProcessId" : "getpid";
        var context = new NativeLibraryContext(library);
        Assert.NotEqual(0, context.GetProcAddress(symbol));
        Parallel.For(0, 64, _ => context.Dispose());
        Assert.Throws<ObjectDisposedException>(() => context.GetProcAddress(symbol));
    }

    [Fact]
    public void MissingModule_IsRejectedAtConstruction()
        => Assert.Throws<DllNotFoundException>(() => new NativeLibraryContext("bgcs-nonexistent-" + Guid.NewGuid().ToString("N")));

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
