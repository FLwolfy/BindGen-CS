using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using BGCS.Runtime;
using Xunit;

namespace BGCS.Runtime.Tests;

public class LibraryLoaderFlowTests
{
    [Fact]
    public void LoadLibrary_WithNameAndLoadInterceptors_ShouldUseInterceptedNameAndReturnPointer()
    {
        string? interceptedName = null;
        LibraryNameInterceptor nameInterceptor = (ref string libraryName) =>
        {
            libraryName = "renamed-lib";
            return true;
        };
        LibraryLoadInterceptor loadInterceptor = (string libraryName, out nint pointer) =>
        {
            interceptedName = libraryName;
            pointer = (nint)0x1234;
            return true;
        };

        try
        {
            LibraryLoader.InterceptLibraryName += nameInterceptor;
            LibraryLoader.InterceptLibraryLoad += loadInterceptor;

            nint handle = LibraryLoader.LoadLibrary(() => "original-lib", null);

            Assert.Equal((nint)0x1234, handle);
            Assert.Equal("renamed-lib", interceptedName);
        }
        finally
        {
            LibraryLoader.InterceptLibraryLoad -= loadInterceptor;
            LibraryLoader.InterceptLibraryName -= nameInterceptor;
        }
    }

    [Fact]
    public void LoadFrom_ShouldResolveRegisteredLibraryName()
    {
        nint expected = (nint)0xCAFE;
        LibraryLoader.LoadFrom("flow-lib", expected);

        nint handle = LibraryLoader.LoadLibrary(() => "flow-lib", null);

        Assert.Equal(expected, handle);
    }

    [Fact]
    public void LoadLibraryEx_WithNativeContextInterceptor_ShouldReturnInjectedContext()
    {
        FakeContext context = new();
        NativeContextInterceptor nativeContextInterceptor = (string libraryName, out INativeContext? resolved) =>
        {
            resolved = libraryName == "ctx-lib" ? context : null;
            return resolved != null;
        };

        try
        {
            LibraryLoader.InterceptNativeContext += nativeContextInterceptor;

            INativeContext resolved = LibraryLoader.LoadLibraryEx(() => "ctx-lib", null);

            Assert.Same(context, resolved);
        }
        finally
        {
            LibraryLoader.InterceptNativeContext -= nativeContextInterceptor;
            context.Dispose();
        }
    }

    [Fact]
    public void IsPathFullyQualified_ShouldMatchRuntimePlatformConventions()
    {
        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        Assert.False(LibraryLoader.IsPathFullyQualified(string.Empty));
        Assert.False(LibraryLoader.IsPathFullyQualified("relative/path/lib"));

        if (isWindows)
        {
            Assert.True(LibraryLoader.IsPathFullyQualified(@"C:\libs\demo.dll"));
            Assert.True(LibraryLoader.IsPathFullyQualified(@"\\server\share\demo.dll"));
            Assert.False(LibraryLoader.IsPathFullyQualified(@"\single-root\demo.dll"));
        }
        else
        {
            Assert.True(LibraryLoader.IsPathFullyQualified("/usr/lib/libdemo.so"));
            Assert.False(LibraryLoader.IsPathFullyQualified("C:\\libs\\demo.dll"));
        }
    }

    [Fact]
    public void GetExtension_ShouldReturnExpectedExtensionForCurrentPlatform()
    {
        string extension = LibraryLoader.GetExtension();
        HashSet<string> knownExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".dll",
            ".dylib",
            ".so",
            ".wasm"
        };

        Assert.Contains(extension, knownExtensions);
    }

    private sealed class FakeContext : INativeContext
    {
        public void Dispose()
        {
        }

        public nint GetProcAddress(string procName) => 0;

        public bool TryGetProcAddress(string procName, out nint procAddress)
        {
            procAddress = 0;
            return false;
        }

        public bool IsExtensionSupported(string extensionName) => false;
    }
}
