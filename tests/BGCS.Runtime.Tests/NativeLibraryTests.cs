using System;
using BGCS.Runtime;
using Xunit;

namespace BGCS.Runtime.Tests;

public sealed class NativeLibraryTests
{
    [Fact]
    public void LoadAndResolveExport_UsesTheHostNativeLoader()
    {
        string libraryName;
        string exportName;
        if (OperatingSystem.IsWindows())
        {
            libraryName = "kernel32.dll";
            exportName = "GetCurrentProcessId";
        }
        else if (OperatingSystem.IsMacOS())
        {
            libraryName = "/usr/lib/libSystem.B.dylib";
            exportName = "malloc";
        }
        else if (OperatingSystem.IsLinux())
        {
            libraryName = "libc.so.6";
            exportName = "malloc";
        }
        else
        {
            throw new PlatformNotSupportedException();
        }

        nint handle = BGCS.Runtime.NativeLibrary.Load(libraryName);
        Assert.NotEqual(0, handle);
        try
        {
            Assert.True(BGCS.Runtime.NativeLibrary.TryGetExport(handle, exportName, out nint address));
            Assert.NotEqual(0, address);
            Assert.Equal(address, BGCS.Runtime.NativeLibrary.GetExport(handle, exportName));
        }
        finally
        {
            Assert.True(BGCS.Runtime.NativeLibrary.Free(handle));
        }
    }
}
