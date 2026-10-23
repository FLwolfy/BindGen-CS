using BGCS.Core.Hosting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Infrastructure.IO;
using BGCS.Infrastructure.Execution;
using Xunit;

namespace BGCS.Infrastructure.Tests;

public sealed class DirectoryMembershipLinkTests : IDisposable
{
    private readonly IHostPathService m_paths = HostTestPaths.Create();
    private readonly string m_root = Path.Combine(PhysicalPathResolver.Resolve(Path.GetTempPath(), HostTestPaths.Create()), "bgcs-membership-links-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> m_links = [];

    public DirectoryMembershipLinkTests() => Directory.CreateDirectory(m_root);

    [Fact]
    public void EveryNameAndExplicitPrivateExclusionKeepTheExistingLengthPrefixedDigest()
    {
        File.WriteAllText(Path.Combine(m_root, "01-λ.any"), "first");
        Directory.CreateDirectory(Path.Combine(m_root, "nested"));
        File.WriteAllText(Path.Combine(m_root, "nested", "no-extension"), "second");
        string excluded = Path.Combine(m_root, ".owned");
        Directory.CreateDirectory(excluded);
        File.WriteAllText(Path.Combine(excluded, "not-an-input"), "private");

        ContentDigest expected = KnownDigest(m_root, PhysicalPathResolver.Resolve(m_root, m_paths), "exists", CaseMarker(m_root),
            "01-λ.any", "file", "", "nested", "directory", "", CaseMarker(Path.Combine(m_root, "nested")), "nested/no-extension", "file", "");
        Assert.Equal(expected, DirectoryMembershipDigest.Capture(m_root, m_paths, [excluded]).digest);
        File.WriteAllText(Path.Combine(excluded, "new-output"), "private");
        Assert.Equal(expected, DirectoryMembershipDigest.Capture(m_root, m_paths, [excluded]).digest);
        File.WriteAllText(Path.Combine(m_root, "header.with-any-extension"), "new candidate");
        Assert.NotEqual(expected, DirectoryMembershipDigest.Capture(m_root, m_paths, [excluded]).digest);
    }

    [Fact]
    public void PermissionChangesKeepMembershipButReplacingAFileWithADirectoryInvalidatesIt()
    {
        string path = Path.Combine(m_root, "value.any");
        File.WriteAllText(path, "bytes");
        FileAttributes original = File.GetAttributes(path);
        DirectoryInputDependency before = DirectoryMembershipDigest.Capture(m_root, m_paths);
        try
        {
            File.SetAttributes(path, original | FileAttributes.ReadOnly);
            Assert.Equal(before, DirectoryMembershipDigest.Capture(m_root, m_paths));
        }
        finally { File.SetAttributes(path, original); }
        File.Delete(path);
        Directory.CreateDirectory(path);
        Assert.NotEqual(before.digest, DirectoryMembershipDigest.Capture(m_root, m_paths).digest);
        Assert.Equal(KnownDigest(m_root, PhysicalPathResolver.Resolve(m_root, m_paths), "exists", CaseMarker(m_root), "value.any", "directory", "", CaseMarker(path)),
            DirectoryMembershipDigest.Capture(m_root, m_paths).digest);
    }

    [Fact]
    public async Task RealDirectoryAliasesAndAncestorCyclesKeepLinkEncodingAndRejectRedirection()
    {
        string include = Path.Combine(m_root, "include"), first = Path.Combine(m_root, "first"), second = Path.Combine(m_root, "second");
        foreach (string path in new[] { include, first, second }) Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(first, "value.any"), "first");
        File.WriteAllText(Path.Combine(second, "value.any"), "other");
        string one = Path.Combine(include, "one"), two = Path.Combine(include, "two"), ancestor = Path.Combine(first, "ancestor");
        await CreateAlias(one, first);
        await CreateAlias(two, first);
        await CreateAlias(ancestor, include);
        Assert.True((new DirectoryInfo(one).Attributes & FileAttributes.ReparsePoint) != 0);
        Assert.Equal(first, new DirectoryInfo(one).LinkTarget);

        DirectoryInputDependency before = DirectoryMembershipDigest.Capture(include, m_paths);
        // A nonempty exclusion set exercises the shared entry-local target
        // observation without changing this exact known graph's encoding.
        Assert.Equal(before, DirectoryMembershipDigest.Capture(include, m_paths, [Path.Combine(m_root, "unreachable-private")]));
        ContentDigest expected = KnownDigest(include, PhysicalPathResolver.Resolve(include, m_paths), "exists", CaseMarker(include),
            "one", "directory", first, first, "two", "directory", first, first,
            CaseMarker(first), "two/ancestor", "directory", include, include, "two/value.any", "file", "");
        Assert.Equal(expected, before.digest);
        SourceSnapshot snapshot = new([], [before]);
        snapshot = InputPathSemanticsSnapshot.Capture(snapshot, m_paths);
        Assert.True(SourceSnapshotVerifier.Matches(snapshot, m_paths));
        DeleteAlias(one);
        m_links.Remove(one);
        await CreateAlias(one, second);
        Assert.Equal(second, PhysicalPathResolver.Resolve(one, m_paths));
        Assert.False(SourceSnapshotVerifier.Matches(snapshot, m_paths));
        Assert.NotEqual(before.digest, DirectoryMembershipDigest.Capture(include, m_paths).digest);
    }

    [Fact]
    public async Task MissingSuffixAndDanglingIdentityRemainObservableWhileCyclicLinksFail()
    {
        string target = Path.Combine(m_root, "target"), link = Path.Combine(m_root, "link");
        Directory.CreateDirectory(target);
        await CreateAlias(link, target);
        string suffix = Path.Combine("missing", "value.def");
        Assert.Equal(Path.Combine(target, suffix), PhysicalPathResolver.Resolve(Path.Combine(link, suffix), m_paths));
        Directory.Delete(target);
        Assert.Equal(target, PhysicalPathResolver.Resolve(link, m_paths));
        string observed = Path.Combine(link, "observed.def"), physicalObserved = Path.Combine(target, "observed.def");
        Assert.Equal(physicalObserved, PhysicalPathResolver.Resolve(observed, m_paths));
        SourceSnapshot absent = new([], []) { absentInputs = [new(observed, physicalObserved)] };
        absent = InputPathSemanticsSnapshot.Capture(absent, m_paths);
        Assert.True(SourceSnapshotVerifier.Matches(absent, m_paths));
        Directory.CreateDirectory(target);
        Assert.True(SourceSnapshotVerifier.Matches(absent, m_paths));
        File.WriteAllText(physicalObserved, "now present");
        Assert.False(SourceSnapshotVerifier.Matches(absent, m_paths));

        string a = Path.Combine(m_root, "cycle-a"), b = Path.Combine(m_root, "cycle-b");
        Directory.CreateDirectory(b);
        await CreateAlias(a, b);
        Directory.Delete(b);
        await CreateAlias(b, a);
        Assert.Throws<IOException>(() => PhysicalPathResolver.Resolve(a, m_paths));
        Assert.Throws<IOException>(() => PhysicalPathResolver.Resolve(Path.Combine(a, "missing"), m_paths));
    }

    private async Task CreateAlias(
        string path,
        string target
    ) {
        Assert.StartsWith(m_root + Path.DirectorySeparatorChar, Path.GetFullPath(path), StringComparison.Ordinal);
        Assert.StartsWith(m_root + Path.DirectorySeparatorChar, Path.GetFullPath(target), StringComparison.Ordinal);
        if (OperatingSystem.IsWindows())
        {
            string executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            string command = "$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path '" + path.Replace("'", "''", StringComparison.Ordinal)
                + "' -Target '" + target.Replace("'", "''", StringComparison.Ordinal) + "' | Out-Null";
            var result = await ProcessExecutor.ExecuteAsync(executable,
                ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(command))],
                m_root, TimeSpan.FromSeconds(30));
            if (new DirectoryInfo(path).LinkTarget is not null) m_links.Add(path);
            Assert.True(result.exitCode == 0, result.standardOutput + Environment.NewLine + result.standardError);
            Assert.Contains(path, m_links);
        }
        else
        {
            Directory.CreateSymbolicLink(path, target);
            m_links.Add(path);
        }
    }

    private string CaseMarker(string directory) => m_paths.GetSemantics(directory).caseSensitive ? "case-sensitive" : "case-insensitive";

    private static ContentDigest KnownDigest(params string[] values)
    {
        using MemoryStream bytes = new();
        using (BinaryWriter writer = new(bytes, Encoding.UTF8, leaveOpen: true))
            foreach (string value in values)
            {
                byte[] encoded = Encoding.UTF8.GetBytes(value.Replace('\\', '/'));
                writer.Write(encoded.Length);
                writer.Write(encoded);
            }
        return new(Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
    }

    public void Dispose()
    {
        for (int index = m_links.Count - 1; index >= 0; index--) DeleteAlias(m_links[index]);
        Directory.Delete(m_root, recursive: true);
    }

    private static void DeleteAlias(string path)
    {
        // Unix unlink must not resolve a dangling or cyclic directory symlink.
        if (OperatingSystem.IsWindows()) Directory.Delete(path);
        else File.Delete(path);
    }
}
