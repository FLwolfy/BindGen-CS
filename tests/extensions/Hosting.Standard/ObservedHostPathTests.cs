using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using BGCS.Core.Hosting;
using BGCS.Core.Inputs;
using BGCS.Hosting.Standard;
using BGCS.Infrastructure.IO;
using Xunit;

namespace BGCS.Hosting.Standard.Tests;

public sealed class ObservedHostPathTests : IDisposable
{
    private readonly string m_root = Path.Combine(Path.GetTempPath(), "bgcs-observed-host-" + Guid.NewGuid().ToString("N"));
    public ObservedHostPathTests() => Directory.CreateDirectory(m_root);

    [Fact]
    public void EachDirectoryOwnsItsComponentComparisonInsteadOfOneOsWideComparer()
    {
        string sensitive = Directory.CreateDirectory(Path.Combine(m_root, "Sensitive")).FullName;
        HostPathService paths = new(directory => new(string.Equals(directory, sensitive, StringComparison.Ordinal)), _ => HostIoFailureKind.Permanent);
        Assert.False(paths.Equals(Path.Combine(sensitive, "Value.h"), Path.Combine(sensitive, "value.h")));
        Assert.True(paths.Equals(Path.Combine(m_root, "File.h"), Path.Combine(m_root, "file.h")));
        Assert.False(paths.comparer.Equals(Path.Combine(sensitive, "Value.h"), Path.Combine(sensitive, "value.h")));
        Assert.True(paths.IsWithin(Path.Combine(sensitive, "nested", "value.h"), sensitive));
        Assert.False(paths.IsWithin(sensitive + "-sibling", sensitive));
        paths.Verify();
    }

    [Fact]
    public void CapturedComparisonFactsHaveStableHashCodesAndMustBeReobserved()
    {
        bool sensitive = false;
        HostPathService paths = new(_ => new(sensitive), _ => HostIoFailureKind.Permanent);
        string file = Path.Combine(m_root, "Value.h");
        string identity = paths.GetIdentityKey(file);
        int captured = paths.comparer.GetHashCode(file);
        sensitive = true;
        Assert.Equal(identity, paths.GetIdentityKey(Path.Combine(m_root, ".", "Value.h")));
        Assert.Equal(captured, paths.comparer.GetHashCode(file));
        Assert.Throws<IOException>(paths.Verify);
    }

    [Fact]
    public void AsciiContainmentStillHonorsSensitiveOwnersAndExactComponentBoundaries()
    {
        string sensitive = Directory.CreateDirectory(Path.Combine(m_root, "sensitive")).FullName;
        HostPathService paths = new(directory => new(directory == sensitive), _ => HostIoFailureKind.Permanent);
        string root = Path.Combine(sensitive, "Folder");
        Assert.True(paths.IsWithin(Path.Combine(root, "value.h"), root));
        Assert.False(paths.IsWithin(Path.Combine(sensitive, "folder", "value.h"), root));
        Assert.False(paths.IsWithin(Path.Combine(sensitive, "Folder-other", "value.h"), root));
        Assert.False(paths.IsWithin(Path.Combine(m_root, "unrelated", "value.h"), root));
        Assert.False(paths.IsWithin(root, root, includeRoot: false));
        paths.Verify();
    }

    [Fact]
    public void UnicodeContainmentKeepsObservedComponentCaseRules()
    {
        string sensitive = Directory.CreateDirectory(Path.Combine(m_root, "sensitive")).FullName;
        HostPathService paths = new(directory => new(directory == sensitive), _ => HostIoFailureKind.Permanent);
        string sensitiveRoot = Path.Combine(sensitive, "Δ");
        Assert.True(paths.IsWithin(Path.Combine(sensitiveRoot, "值.h"), sensitiveRoot));
        Assert.False(paths.IsWithin(Path.Combine(sensitive, "δ", "值.h"), sensitiveRoot));
        Assert.True(paths.IsWithin(Path.Combine(m_root, "δ", "值.h"), Path.Combine(m_root, "Δ")));
        Assert.False(paths.IsWithin(Path.Combine(m_root, "其他", "值.h"), sensitiveRoot));
        paths.Verify();
    }

    [Fact]
    public void ObservedProviderUsesTheActualDirectorySemanticsAndAnExplicitManagedHost()
    {
        using HostServices host = new HostServicesProvider().Create(new(m_root, JsonSerializer.SerializeToElement(new { })));
        HostPathSemantics semantics = host.paths.GetSemantics(m_root);
        Assert.Equal(!semantics.caseSensitive, host.paths.Equals(Path.Combine(m_root, "Value.h"), Path.Combine(m_root, "value.h")));
        Assert.True(File.Exists(host.execution.managedHostExecutable));
        Assert.NotEmpty(host.execution.hostRuntimeIdentifier);
        Assert.Equal(Path.GetFullPath(m_root), host.paths.GetIdentity(m_root).logicalPath);
        host.paths.Verify();
    }

    [Fact]
    public void MissingComponentsUseTheObservedExistingOwnerWithoutCreatingFiles()
    {
        HostPathService paths = new(_ => new(true), _ => HostIoFailureKind.Permanent);
        string absent = Path.Combine(m_root, "absent", "nested", "value.h");
        HostPathIdentity identity = paths.GetIdentity(absent);
        Assert.Equal(Path.GetFullPath(absent), identity.physicalPath);
        Assert.False(Directory.Exists(Path.Combine(m_root, "absent")));
        paths.Verify();
    }

    [Fact]
    public async Task HostLifetimeDisposesItsOwnedAsynchronousPathFacilityExactlyOnce()
    {
        AsyncPaths paths = new();
        HostServices host = new(paths, new("fixture-host", "fixture-executable", true));
        Assert.Throws<InvalidOperationException>(host.Dispose);
        await host.DisposeAsync();
        await host.DisposeAsync();
        Assert.Equal(1, paths.disposals);
    }

    [Fact]
    public void WindowsLongDirectoryPathsKeepRealCaseSemanticsAndLogicalIdentities()
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = m_root;
        while (directory.Length < 340)
            directory = Path.Combine(directory, "long-" + new string('x', 70));
        Directory.CreateDirectory(directory);
        SetCaseSensitivity(@"\\?\" + directory, enabled: true);
        string upper = Path.Combine(directory, "A.h");
        string lower = Path.Combine(directory, "a.h");
        File.WriteAllText(upper, "upper");
        File.WriteAllText(lower, "lower");
        using HostServices host = new HostServicesProvider().Create(new(m_root, JsonSerializer.SerializeToElement(new { })));
        Assert.True(host.paths.GetSemantics(directory).caseSensitive);
        Assert.False(host.paths.Equals(upper, lower));
        Assert.Equal(Path.GetFullPath(upper), host.paths.GetIdentity(upper).logicalPath);
        Assert.Equal(Path.Combine("..", "a.h"), host.paths.GetRelativePath(upper, lower));
        Assert.Equal("upper", File.ReadAllText(upper));
        Assert.Equal("lower", File.ReadAllText(lower));
        host.paths.Verify();
    }

    [Fact]
    public void WindowsCaseSensitiveDirectoryIsObservedWithoutAnOsWideCaseAssumption()
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = Directory.CreateDirectory(Path.Combine(m_root, "actual-sensitive")).FullName;
        using (SafeFileHandle handle = CreateFileW(directory, 0x180, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero))
        {
            if (handle.IsInvalid) throw new IOException("Cannot open the owned case-semantics fixture.", new Win32Exception(Marshal.GetLastPInvokeError()));
            uint enabled = 1;
            if (!SetFileInformationByHandle(handle, 23, ref enabled, sizeof(uint)))
                throw new IOException("The Windows runner cannot establish the required case-sensitive directory fixture.", new Win32Exception(Marshal.GetLastPInvokeError()));
        }
        File.WriteAllText(Path.Combine(directory, "A.h"), "upper");
        File.WriteAllText(Path.Combine(directory, "a.h"), "lower");
        using HostServices host = new HostServicesProvider().Create(new(m_root, JsonSerializer.SerializeToElement(new { })));
        Assert.True(host.paths.GetSemantics(directory).caseSensitive);
        Assert.False(host.paths.Equals(Path.Combine(directory, "A.h"), Path.Combine(directory, "a.h")));
        Assert.Equal(Path.Combine("..", "a.h"), host.paths.GetRelativePath(Path.Combine(directory, "A.h"), Path.Combine(directory, "a.h")));
        Assert.Equal("upper", File.ReadAllText(Path.Combine(directory, "A.h")));
        Assert.Equal("lower", File.ReadAllText(Path.Combine(directory, "a.h")));
        host.paths.Verify();
    }

    [Fact]
    public void RelativePathsPreserveSensitiveSiblingNamesEvenOnAnInsensitiveOperatingSystem()
    {
        string owner = Directory.CreateDirectory(Path.Combine(m_root, "owner")).FullName;
        HostPathService paths = new(directory => new(string.Equals(directory, owner, StringComparison.Ordinal)), _ => HostIoFailureKind.Permanent);
        Assert.Equal(Path.Combine("..", "a"), paths.GetRelativePath(Path.Combine(owner, "A"), Path.Combine(owner, "a")));
        Assert.Equal(".", paths.GetRelativePath(Path.Combine(m_root, "A"), Path.Combine(m_root, "a")));
    }

    [Fact]
    public void AFormerlyAbsentDirectoryCannotChangeAnExistingDictionaryHashWhenCreated()
    {
        string owner = Path.Combine(m_root, "appearing");
        HostPathService paths = new(directory => new(Directory.Exists(owner) && directory == owner), _ => HostIoFailureKind.Permanent);
        string file = Path.Combine(owner, "Value.h");
        string identity = paths.GetIdentityKey(file);
        int key = paths.comparer.GetHashCode(file);
        Directory.CreateDirectory(owner);
        Assert.Equal(identity, paths.GetIdentityKey(file));
        Assert.Equal(key, paths.comparer.GetHashCode(file));
        Assert.Throws<IOException>(paths.Verify);
    }

    [Fact]
    public void ARealCasePolicyChangeBetweenSnapshotsStillFailsTheOuterFence()
    {
        if (!OperatingSystem.IsWindows()) return;
        string changing = Directory.CreateDirectory(Path.Combine(m_root, "changing")).FullName;
        SetCaseSensitivity(changing, enabled: false);
        string file = Path.Combine(m_root, "input.h");
        File.WriteAllText(file, "int input(void);");
        using HostServices host = new HostServicesProvider().Create(new(m_root, JsonSerializer.SerializeToElement(new { })));
        string name = Path.Combine(changing, "Value.h");
        string identity = host.paths.GetIdentityKey(name);
        int hash = host.paths.comparer.GetHashCode(name);
        SourceSnapshot input = new ExternalInputSnapshotBuilder().Capture([new(file, ExternalInputKind.File)], m_root, host.paths);
        Assert.False(host.paths.GetSemantics(changing).caseSensitive);
        string? mismatch = SourceSnapshotBatch.GetMismatch(Snapshots(), host.paths);
        Assert.Contains("semantics changed", mismatch!, StringComparison.Ordinal);
        Assert.Equal(identity, host.paths.GetIdentityKey(name));
        Assert.Equal(hash, host.paths.comparer.GetHashCode(name));

        IEnumerable<SourceSnapshot> Snapshots()
        {
            yield return input;
            SetCaseSensitivity(changing, enabled: true);
            yield return new([], []);
        }
    }

    private static void SetCaseSensitivity(string directory, bool enabled)
    {
        using SafeFileHandle handle = CreateFileW(directory, 0x180, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid) throw new IOException("Cannot open the owned case-policy fixture.", new Win32Exception(Marshal.GetLastPInvokeError()));
        uint flags = enabled ? 1u : 0u;
        if (!SetFileInformationByHandle(handle, 23, ref flags, sizeof(uint)))
            throw new IOException("Cannot change the owned case-policy fixture.", new Win32Exception(Marshal.GetLastPInvokeError()));
    }

    [Fact]
    public void BootstrapRejectsRelativeBasesAndUnknownSettings()
    {
        HostServicesProvider provider = new();
        Assert.Throws<ArgumentException>(() => provider.Create(new("relative", JsonSerializer.SerializeToElement(new { }))));
        Assert.Throws<ArgumentException>(() => provider.Create(new(m_root, JsonSerializer.SerializeToElement(new { unknown = true }))));
    }

    private sealed class AsyncPaths : IHostPathService, IAsyncDisposable
    {
        private readonly HostPathService m_paths = new(_ => new(true), _ => HostIoFailureKind.Permanent);
        internal int disposals;
        public System.Collections.Generic.IEqualityComparer<string> comparer => m_paths.comparer;
        public HostPathIdentity GetIdentity(string path) => m_paths.GetIdentity(path);
        public HostPathSemantics GetSemantics(string directory) => m_paths.GetSemantics(directory);
        public string GetIdentityKey(string path) => m_paths.GetIdentityKey(path);
        public bool Equals(string left, string right) => m_paths.Equals(left, right);
        public bool IsWithin(string candidate, string root, bool includeRoot = true) => m_paths.IsWithin(candidate, root, includeRoot);
        public string GetRelativePath(string baseDirectory, string path) => m_paths.GetRelativePath(baseDirectory, path);
        public HostIoFailureKind ClassifyIoFailure(Exception failure) => m_paths.ClassifyIoFailure(failure);
        public void Verify() => m_paths.Verify();
        public ValueTask DisposeAsync() { disposals++; return ValueTask.CompletedTask; }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, ref uint information, uint size);

    public void Dispose() => Directory.Delete(m_root, recursive: true);
}
