using BGCS.Core.Hosting;
using System;
using System.IO;
using System.Security.Cryptography;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Infrastructure.IO;
using Xunit;

namespace BGCS.Infrastructure.Tests;

public sealed class InputSnapshotTests : IDisposable
{
    private readonly IHostPathService m_paths = HostTestPaths.Create();
    private readonly string m_root = Path.Combine(Path.GetTempPath(), "bgcs-input-snapshot-" + Guid.NewGuid().ToString("N"));

    public InputSnapshotTests() => Directory.CreateDirectory(m_root);

    [Fact]
    public void AddedHeaderWithAnyExtension_InvalidatesSearchMembership()
    {
        Directory.CreateDirectory(Path.Combine(m_root, "first"));
        string input = Path.Combine(m_root, "second", "value.def");
        Directory.CreateDirectory(Path.GetDirectoryName(input)!);
        File.WriteAllText(input, "original");
        SourceSnapshot snapshot = Snapshot(input);
        Assert.True(SourceSnapshotVerifier.Matches(snapshot, m_paths));
        File.WriteAllText(Path.Combine(m_root, "first", "value.def"), "shadow");
        Assert.False(SourceSnapshotVerifier.Matches(snapshot, m_paths));
    }

    [Fact]
    public void ChangedBytesWithTheSameLengthAndTimestamp_InvalidatesFileIdentity()
    {
        string input = Path.Combine(m_root, "value.h");
        File.WriteAllText(input, "1234");
        DateTime timestamp = File.GetLastWriteTimeUtc(input);
        SourceSnapshot snapshot = Snapshot(input);
        File.WriteAllText(input, "5678");
        File.SetLastWriteTimeUtc(input, timestamp);
        Assert.False(SourceSnapshotVerifier.Matches(snapshot, m_paths));
    }

    [Fact]
    public void MissingIncludeDirectory_CreatingItInvalidatesTheSnapshot()
    {
        string missing = Path.Combine(m_root, "missing");
        SourceSnapshot snapshot = new([], [DirectoryMembershipDigest.Capture(missing, m_paths)]);
        snapshot = InputPathSemanticsSnapshot.Capture(snapshot, m_paths);
        Assert.True(SourceSnapshotVerifier.Matches(snapshot, m_paths));
        Directory.CreateDirectory(missing);
        Assert.False(SourceSnapshotVerifier.Matches(snapshot, m_paths));
    }

    [Fact]
    public void ContentsDoNotChangeMembership_ButChangingNamesDoes()
    {
        string input = Path.Combine(m_root, "value.h");
        File.WriteAllText(input, "before");
        DirectoryInputDependency before = DirectoryMembershipDigest.Capture(m_root, m_paths);
        File.WriteAllText(input, "after");
        Assert.Equal(before, DirectoryMembershipDigest.Capture(m_root, m_paths));
        File.Move(input, Path.Combine(m_root, "renamed.h"));
        Assert.NotEqual(before, DirectoryMembershipDigest.Capture(m_root, m_paths));
    }

    [Fact]
    public void PrivateCandidateMembership_IsExcludedButItsReadBytesRemainVerified()
    {
        string candidate = Path.Combine(m_root, ".bgcs-staging-private");
        Directory.CreateDirectory(candidate);
        string header = Path.Combine(candidate, "bridge.h");
        File.WriteAllText(header, "int value(void);");
        byte[] bytes = File.ReadAllBytes(header);
        SourceSnapshot snapshot = new([new(header, PhysicalPathResolver.Resolve(header, m_paths), bytes.Length,
            new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(bytes))))], [DirectoryMembershipDigest.Capture(m_root, m_paths, [candidate])])
        { excludedDirectories = [candidate] };
        snapshot = InputPathSemanticsSnapshot.Capture(snapshot, m_paths);
        File.WriteAllText(Path.Combine(candidate, "Bindings.cs"), "public class Bindings {}");
        Assert.True(SourceSnapshotVerifier.Matches(snapshot, m_paths));
        File.AppendAllText(header, " // changed");
        Assert.False(SourceSnapshotVerifier.Matches(snapshot, m_paths));
        File.WriteAllBytes(header, bytes);
        File.WriteAllText(Path.Combine(m_root, "shadow.header"), "new candidate");
        Assert.False(SourceSnapshotVerifier.Matches(snapshot, m_paths));
    }

    private SourceSnapshot Snapshot(string input)
    {
        byte[] bytes = File.ReadAllBytes(input);
        return InputPathSemanticsSnapshot.Capture(new([new(input, PhysicalPathResolver.Resolve(input, m_paths), bytes.Length,
            new ContentDigest(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()))],
            [DirectoryMembershipDigest.Capture(m_root, m_paths)]), m_paths);
    }

    public void Dispose() => Directory.Delete(m_root, recursive: true);
}
