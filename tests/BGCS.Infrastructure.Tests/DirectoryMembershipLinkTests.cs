using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Infrastructure.IO;
using BGCS.Infrastructure.Execution;
using Xunit;

namespace BGCS.Infrastructure.Tests;

public sealed class DirectoryMembershipLinkTests : IDisposable
{
    private readonly string m_root = Path.Combine(PhysicalPathResolver.Resolve(Path.GetTempPath()), "bgcs-membership-links-" + Guid.NewGuid().ToString("N"));
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

        ContentDigest expected = KnownDigest(m_root, PhysicalPathResolver.Resolve(m_root), "exists",
            "01-λ.any", "file", "", "nested", "directory", "", "nested/no-extension", "file", "");
        Assert.Equal(expected, DirectoryMembershipDigest.Capture(m_root, [excluded]).digest);
        File.WriteAllText(Path.Combine(excluded, "new-output"), "private");
        Assert.Equal(expected, DirectoryMembershipDigest.Capture(m_root, [excluded]).digest);
        File.WriteAllText(Path.Combine(m_root, "header.with-any-extension"), "new candidate");
        Assert.NotEqual(expected, DirectoryMembershipDigest.Capture(m_root, [excluded]).digest);
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

        DirectoryInputDependency before = DirectoryMembershipDigest.Capture(include);
        ContentDigest expected = KnownDigest(include, PhysicalPathResolver.Resolve(include), "exists",
            "one", "directory", first, first, "two", "directory", first, first,
            "two/ancestor", "directory", include, include, "two/value.any", "file", "");
        Assert.Equal(expected, before.digest);
        SourceSnapshot snapshot = new([], [before]);
        Assert.True(SourceSnapshotVerifier.Matches(snapshot));
        DeleteAlias(one);
        m_links.Remove(one);
        await CreateAlias(one, second);
        Assert.Equal(second, PhysicalPathResolver.Resolve(one));
        Assert.False(SourceSnapshotVerifier.Matches(snapshot));
        Assert.NotEqual(before.digest, DirectoryMembershipDigest.Capture(include).digest);
    }

    [Fact]
    public async Task MissingSuffixAndDanglingIdentityRemainObservableWhileCyclicLinksFail()
    {
        string target = Path.Combine(m_root, "target"), link = Path.Combine(m_root, "link");
        Directory.CreateDirectory(target);
        await CreateAlias(link, target);
        string suffix = Path.Combine("missing", "value.def");
        Assert.Equal(Path.Combine(target, suffix), PhysicalPathResolver.Resolve(Path.Combine(link, suffix)));
        Directory.Delete(target);
        Assert.Equal(target, PhysicalPathResolver.Resolve(link));
        string observed = Path.Combine(link, "observed.def"), physicalObserved = Path.Combine(target, "observed.def");
        Assert.Equal(physicalObserved, PhysicalPathResolver.Resolve(observed));
        SourceSnapshot absent = new([], []) { absentInputs = [new(observed, physicalObserved)] };
        Assert.True(SourceSnapshotVerifier.Matches(absent));
        Directory.CreateDirectory(target);
        Assert.True(SourceSnapshotVerifier.Matches(absent));
        File.WriteAllText(physicalObserved, "now present");
        Assert.False(SourceSnapshotVerifier.Matches(absent));

        string a = Path.Combine(m_root, "cycle-a"), b = Path.Combine(m_root, "cycle-b");
        Directory.CreateDirectory(b);
        await CreateAlias(a, b);
        Directory.Delete(b);
        await CreateAlias(b, a);
        Assert.Throws<IOException>(() => PhysicalPathResolver.Resolve(a));
        Assert.Throws<IOException>(() => PhysicalPathResolver.Resolve(Path.Combine(a, "missing")));
    }

    private async Task CreateAlias(string path, string target)
    {
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
