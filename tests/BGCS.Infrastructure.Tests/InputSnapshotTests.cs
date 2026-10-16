using System;
using System.IO;
using System.Security.Cryptography;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Infrastructure.IO;
using Xunit;

namespace BGCS.Infrastructure.Tests;

public sealed class InputSnapshotTests : IDisposable
{
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
        Assert.True(SourceSnapshotVerifier.Matches(snapshot));
        File.WriteAllText(Path.Combine(m_root, "first", "value.def"), "shadow");
        Assert.False(SourceSnapshotVerifier.Matches(snapshot));
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
        Assert.False(SourceSnapshotVerifier.Matches(snapshot));
    }

    [Fact]
    public void MissingIncludeDirectory_CreatingItInvalidatesTheSnapshot()
    {
        string missing = Path.Combine(m_root, "missing");
        SourceSnapshot snapshot = new([], [DirectoryMembershipDigest.Capture(missing)]);
        Assert.True(SourceSnapshotVerifier.Matches(snapshot));
        Directory.CreateDirectory(missing);
        Assert.False(SourceSnapshotVerifier.Matches(snapshot));
    }

    [Fact]
    public void ContentsDoNotChangeMembership_ButChangingNamesDoes()
    {
        string input = Path.Combine(m_root, "value.h");
        File.WriteAllText(input, "before");
        DirectoryInputDependency before = DirectoryMembershipDigest.Capture(m_root);
        File.WriteAllText(input, "after");
        Assert.Equal(before, DirectoryMembershipDigest.Capture(m_root));
        File.Move(input, Path.Combine(m_root, "renamed.h"));
        Assert.NotEqual(before, DirectoryMembershipDigest.Capture(m_root));
    }

    [Fact]
    public void PrivateCandidateMembership_IsExcludedButItsReadBytesRemainVerified()
    {
        string candidate = Path.Combine(m_root, ".bgcs-staging-private");
        Directory.CreateDirectory(candidate);
        string header = Path.Combine(candidate, "bridge.h");
        File.WriteAllText(header, "int value(void);");
        byte[] bytes = File.ReadAllBytes(header);
        SourceSnapshot snapshot = new([new(header, PhysicalPathResolver.Resolve(header), bytes.Length,
            new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(bytes))))], [DirectoryMembershipDigest.Capture(m_root, [candidate])])
        { excludedDirectories = [candidate] };
        File.WriteAllText(Path.Combine(candidate, "Bindings.cs"), "public class Bindings {}");
        Assert.True(SourceSnapshotVerifier.Matches(snapshot));
        File.AppendAllText(header, " // changed");
        Assert.False(SourceSnapshotVerifier.Matches(snapshot));
        File.WriteAllBytes(header, bytes);
        File.WriteAllText(Path.Combine(m_root, "shadow.header"), "new candidate");
        Assert.False(SourceSnapshotVerifier.Matches(snapshot));
    }

    private SourceSnapshot Snapshot(string input)
    {
        byte[] bytes = File.ReadAllBytes(input);
        return new([new(input, PhysicalPathResolver.Resolve(input), bytes.Length,
            new ContentDigest(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()))],
            [DirectoryMembershipDigest.Capture(m_root)]);
    }

    public void Dispose() => Directory.Delete(m_root, recursive: true);
}
