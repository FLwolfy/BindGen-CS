using System;
using System.IO;
using BGCS.Contracts.Inputs;
using BGCS.Infrastructure.IO;
using Xunit;

namespace BGCS.Infrastructure.Tests;

public sealed class ExternalInputSnapshotTests : IDisposable
{
    private readonly string m_root = Path.Combine(Path.GetTempPath(), "bgcs-external-inputs-" + Guid.NewGuid().ToString("N"));
    public ExternalInputSnapshotTests() => Directory.CreateDirectory(m_root);

    [Fact]
    public void DirectoryClosure_TracksNestedBytesAndMembershipIndependentOfTimestamps()
    {
        Directory.CreateDirectory(Path.Combine(m_root, "inputs", "nested"));
        string file = Path.Combine(m_root, "inputs", "nested", "state.any");
        File.WriteAllText(file, "1234");
        DateTime timestamp = File.GetLastWriteTimeUtc(file);
        SourceSnapshot snapshot = new ExternalInputSnapshotBuilder().Capture([new("inputs", ExternalInputKind.Directory)], m_root);
        Assert.Single(snapshot.files);
        Assert.True(SourceSnapshotVerifier.Matches(snapshot));
        File.WriteAllText(file, "5678");
        File.SetLastWriteTimeUtc(file, timestamp);
        Assert.False(SourceSnapshotVerifier.Matches(snapshot));
        File.WriteAllText(file, "1234");
        Assert.True(SourceSnapshotVerifier.Matches(snapshot));
        File.WriteAllText(Path.Combine(m_root, "inputs", "new.def"), "new input");
        Assert.False(SourceSnapshotVerifier.Matches(snapshot));
    }

    [Theory]
    [InlineData(ExternalInputKind.File)]
    [InlineData(ExternalInputKind.Directory)]
    public void AllowedAbsence_IsInvalidatedByLaterCreationOfEitherFilesystemKind(ExternalInputKind kind)
    {
        string path = Path.Combine(m_root, "missing");
        SourceSnapshot snapshot = new ExternalInputSnapshotBuilder().Capture([new("missing", kind, allowMissing: true)], m_root);
        Assert.Single(snapshot.absentInputs);
        Assert.True(SourceSnapshotVerifier.Matches(snapshot));
        File.WriteAllText(path, "now present");
        Assert.False(SourceSnapshotVerifier.Matches(snapshot));
        File.Delete(path);
        Directory.CreateDirectory(path);
        Assert.False(SourceSnapshotVerifier.Matches(snapshot));
    }

    [Fact]
    public void MissingRequiredAndWrongKindInputs_AreRejected()
    {
        ExternalInputSnapshotBuilder builder = new();
        Assert.Throws<FileNotFoundException>(() => builder.Capture([new("absent", ExternalInputKind.File)], m_root));
        File.WriteAllText(Path.Combine(m_root, "value"), "bytes");
        Assert.Throws<IOException>(() => builder.Capture([new("value", ExternalInputKind.Directory, allowMissing: true)], m_root));
    }

    [Fact]
    public void ExtensionlessSdkInputsAndEqualTimestampByteChangesRemainObserved()
    {
        string include = Path.Combine(m_root, "include");
        Directory.CreateDirectory(include);
        string header = Path.Combine(include, "vector");
        File.WriteAllText(header, "struct First;");
        DateTime timestamp = File.GetLastWriteTimeUtc(header);
        SourceSnapshot snapshot = new ExternalInputSnapshotBuilder().Capture([new("include", ExternalInputKind.Directory)], m_root);
        Assert.Equal(header, Assert.Single(snapshot.files).path);
        Assert.True(SourceSnapshotVerifier.Matches(snapshot));
        File.WriteAllText(header, "struct Other;");
        File.SetLastWriteTimeUtc(header, timestamp);
        Assert.False(SourceSnapshotVerifier.Matches(snapshot));
    }

    [Fact]
    public void DirectoryAliasesAndAncestorCyclesPreserveDependencyBytesWithoutInfiniteTraversal()
    {
        if (OperatingSystem.IsWindows()) return;
        string include = Path.Combine(m_root, "include"), sdk = Path.Combine(m_root, "sdk");
        Directory.CreateDirectory(include);
        Directory.CreateDirectory(sdk);
        string header = Path.Combine(sdk, "vector");
        File.WriteAllText(header, "struct Original;");
        Directory.CreateSymbolicLink(Path.Combine(include, "first"), sdk);
        Directory.CreateSymbolicLink(Path.Combine(include, "second"), sdk);
        Directory.CreateSymbolicLink(Path.Combine(sdk, "ancestor"), include);
        SourceSnapshot snapshot = new ExternalInputSnapshotBuilder().Capture([new("include", ExternalInputKind.Directory)], m_root);
        Assert.Equal(PhysicalPathResolver.Resolve(header), Assert.Single(snapshot.files).resolvedPath);
        Assert.True(SourceSnapshotVerifier.Matches(snapshot));
        File.WriteAllText(header, "struct Modified;");
        Assert.False(SourceSnapshotVerifier.Matches(snapshot));
    }

    public void Dispose() => Directory.Delete(m_root, recursive: true);
}
