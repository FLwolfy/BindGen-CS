using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using BGCS.Frontend.Clang;
using Xunit;

namespace BGCS.Frontend.Clang.Tests;

public sealed class ClangResourceHeaderIntegrityTests
{
    [Fact]
    public void ARelativeCacheCannotWriteIntoTheNativeInvocationDirectory()
    {
        string relative = "bgcs-relative-parser-cache-" + Guid.NewGuid().ToString("N");
        Assert.False(Directory.Exists(relative));
        Assert.Throws<ArgumentException>(() => Extract(Bundle(), relative));
        Assert.False(Directory.Exists(relative));
    }

    [Fact]
    public void ExistingExtraction_IsReusedOnlyAfterCompleteContentVerification()
    {
        WithCache((cache, bundle) =>
        {
            string directory = Extract(bundle, cache);
            Assert.Equal("builtin sentinel", File.ReadAllText(Path.Combine(directory, "include", "stddef.h")));
            Assert.Equal(directory, Extract(bundle, cache));
            Assert.Equal(2, Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Length);
        });
    }

    [Theory]
    [InlineData("same-size-content")]
    [InlineData("missing-file")]
    [InlineData("extra-file")]
    [InlineData("extra-directory")]
    public void ExistingSentinel_DoesNotMakeAnAlteredBundleTrusted(string change)
    {
        WithCache((cache, bundle) =>
        {
            string directory = Extract(bundle, cache);
            string other = Path.Combine(directory, "include", "nested", "stdint.h");
            if (change == "same-size-content") File.WriteAllText(other, "X" + File.ReadAllText(other)[1..]);
            else if (change == "missing-file") File.Delete(other);
            else if (change == "extra-file") File.WriteAllText(Path.Combine(directory, "injected.h"), "injected");
            else Directory.CreateDirectory(Path.Combine(directory, "unexpected"));
            Assert.Throws<InvalidDataException>(() => Extract(bundle, cache));
            Assert.Equal("builtin sentinel", File.ReadAllText(Path.Combine(directory, "include", "stddef.h")));
        });
    }

    [Theory]
    [InlineData("../escaped.h")]
    [InlineData("/escaped.h")]
    [InlineData("include/../../escaped.h")]
    [InlineData("include/evil:stream")]
    public void UnsafeArchivePaths_AreRejectedBeforeExtraction(string path)
    {
        WithCache((cache, _) =>
        {
            byte[] bundle = Bundle(path);
            Assert.Throws<InvalidDataException>(() => Extract(bundle, cache));
            Assert.Empty(Directory.GetFileSystemEntries(cache));
            Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(cache)!, "escaped.h")));
        });
    }

    [Fact]
    public void AFileUsedAsParentDirectory_IsRejectedBeforeExtraction()
    {
        WithCache((cache, _) =>
        {
            byte[] bundle = Bundle("include/nested");
            Assert.Throws<InvalidDataException>(() => Extract(bundle, cache));
            Assert.Empty(Directory.GetFileSystemEntries(cache));
        });
    }

    [Fact]
    public void ExistingPhysicalFile_CannotStandInForAResourceDirectory()
    {
        WithCache((cache, bundle) =>
        {
            string destination = Path.Combine(cache, Convert.ToHexString(SHA256.HashData(bundle)));
            File.WriteAllText(destination, "not a directory");
            Assert.Throws<InvalidDataException>(() => Extract(bundle, cache));
        });
    }

    [Fact]
    public void SymbolicLinkArchiveEntries_AreRejectedBeforeExtraction()
    {
        WithCache((cache, _) =>
        {
            using MemoryStream stream = new();
            using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                using (StreamWriter sentinel = new(archive.CreateEntry("include/stddef.h").Open())) sentinel.Write("sentinel");
                ZipArchiveEntry link = archive.CreateEntry("include/escape");
                link.ExternalAttributes = 0xA000 << 16;
                using StreamWriter writer = new(link.Open());
                writer.Write("../../outside");
            }
            Assert.Throws<InvalidDataException>(() => Extract(stream.ToArray(), cache));
            Assert.Empty(Directory.GetFileSystemEntries(cache));
        });
    }

    [Fact]
    public void ExistingUnixDirectorySymlink_IsRejectedWithoutReadingOutsideCache()
    {
        if (OperatingSystem.IsWindows()) return; // This row exercises Unix links without requiring Windows link privileges.
        WithCache((cache, bundle) =>
        {
            string destination = Extract(bundle, cache);
            string link = Path.Combine(destination, "include", "nested");
            Directory.Delete(link, recursive: true);
            string outside = Path.Combine(cache, "outside");
            Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "stdint.h"), "outside cache");
            Directory.CreateSymbolicLink(link, outside);
            Assert.Throws<InvalidDataException>(() => Extract(bundle, cache));
            Assert.Equal("outside cache", File.ReadAllText(Path.Combine(outside, "stdint.h")));
        });
    }

    private static string Extract(byte[] bundle, string cache)
    {
        MethodInfo method = typeof(ClangFrontend).Assembly.GetType("BGCS.Frontend.Clang.Interop.ClangResourceHeaders", throwOnError: true)!
            .GetMethod("EnsureExtracted", BindingFlags.Static | BindingFlags.NonPublic)!;
        using MemoryStream stream = new(bundle, writable: false);
        try { return (string)method.Invoke(null, [stream, cache])!; }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        { ExceptionDispatchInfo.Capture(exception.InnerException).Throw(); throw; }
    }

    private static void WithCache(Action<string, byte[]> test)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-header-integrity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { test(root, Bundle()); }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static byte[] Bundle(string? additionalPath = null)
    {
        using MemoryStream stream = new();
        using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write("include/stddef.h", "builtin sentinel");
            Write("include/nested/stdint.h", "locked numeric header");
            if (additionalPath is not null) Write(additionalPath, "unexpected");
            void Write(string path, string content)
            {
                using StreamWriter writer = new(archive.CreateEntry(path).Open());
                writer.Write(content);
            }
        }
        return stream.ToArray();
    }
}
