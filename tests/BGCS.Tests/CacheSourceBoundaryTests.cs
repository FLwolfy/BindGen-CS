using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core.Hosting;
using BGCS.Core.Plugins;
using BGCS.Hosting.Standard;
using Microsoft.Win32.SafeHandles;
using Xunit;

namespace BGCS.Tests;

public sealed class CacheSourceBoundaryTests
{
    [Fact]
    public async Task ExplicitIncludeRootEqualToCacheIsRejectedBeforeCreatingCacheFiles()
    {
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "bgcs-cache-source-boundary-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string shared = Directory.CreateDirectory(Path.Combine(root, "declared-input")).FullName;
            string sentinel = Path.Combine(shared, "user-input.keep");
            await File.WriteAllTextAsync(sentinel, "original user input");
            await File.WriteAllTextAsync(Path.Combine(root, "native.h"), "int native_add(int left, int right);\n");
            await using BindingHost host = BindingHost.CreateBuilder()
                .RegisterFactory<IHostServicesProvider>(new("boundary.host", ExtensionPort.HostServicesProvider), _ => new HostServicesProvider())
                .Build();
            GenerationResult result = await host.GenerateAsync(new(Configuration("declared-input", "declared-input"), root));
            Assert.False(result.success);
            Assert.Contains("generation cache overlaps", result.GetDiagnosticSummary(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal("original user input", await File.ReadAllTextAsync(sentinel));
            Assert.Equal([sentinel], Directory.GetFiles(shared));
            Assert.False(Directory.Exists(Path.Combine(root, "Generated")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ActualCaseSensitiveSiblingInputAndCacheDoNotUseTheRequestBasesInsensitiveRules()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "bgcs-cache-sensitive-boundary-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string names = Directory.CreateDirectory(Path.Combine(root, "sensitive-names")).FullName;
            using (SafeFileHandle handle = CreateFileW(names, 0x180, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero))
            {
                if (handle.IsInvalid) throw new IOException("Cannot open the owned case-semantics fixture.", new Win32Exception(Marshal.GetLastPInvokeError()));
                uint enabled = 1;
                if (!SetFileInformationByHandle(handle, 23, ref enabled, sizeof(uint)))
                    throw new IOException("The Windows runner cannot establish the required case-sensitive directory fixture.", new Win32Exception(Marshal.GetLastPInvokeError()));
            }
            string input = Directory.CreateDirectory(Path.Combine(names, "Cache")).FullName;
            string cache = Directory.CreateDirectory(Path.Combine(names, "cache")).FullName;
            await File.WriteAllTextAsync(Path.Combine(root, "native.h"), "int native_add(int left, int right);\n");
            await File.WriteAllTextAsync(Path.Combine(input, "user-input.keep"), "protected source");
            using (HostServices facts = new HostServicesProvider().Create(new(root, JsonSerializer.SerializeToElement(new { }))))
            {
                Assert.False(facts.paths.GetSemantics(root).caseSensitive);
                Assert.True(facts.paths.GetSemantics(names).caseSensitive);
                Assert.False(facts.paths.Equals(input, cache));
                Assert.False(facts.paths.IsWithin(input, cache));
            }
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            GenerationResult result = await host.GenerateAsync(new(Configuration(input, cache), root));
            Assert.True(result.success, result.GetDiagnosticSummary());
            Assert.Equal("protected source", await File.ReadAllTextAsync(Path.Combine(input, "user-input.keep")));
            Assert.NotEmpty(result.artifacts);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static BindingConfiguration Configuration(
        string include,
        string cache
    ) => new()
    {
        schemaVersion = 2,
        library = new() { id = "cache_boundary", nativeName = "cache_boundary" },
        source = new() { headers = ["native.h"], includeDirectories = [include], parseMacros = false },
        api = new() { @namespace = "Generated.CacheBoundary", name = "BoundaryApi" },
        cache = new() { enabled = true, directory = cache }
    };

    private static string WorkerRoot()
    {
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "BindGen-CS.slnx"))) repository = repository.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(repository!.FullName, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string path,
        uint access,
        uint share,
        IntPtr security,
        uint creation,
        uint flags,
        IntPtr template
    );
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(
        SafeFileHandle handle,
        int informationClass,
        ref uint information,
        uint size
    );
}
