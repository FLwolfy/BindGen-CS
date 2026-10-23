using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using BGCS.Core.Hosting;
using BGCS.Core.Inputs;
using BGCS.Hosting.Standard;
using BGCS.Infrastructure.IO;
using Xunit;

namespace BGCS.Infrastructure.Tests;

public sealed class InputPathSemanticsTests : IDisposable
{
    private readonly string m_root = Path.Combine(Path.GetTempPath(), "bgcs-input-path-facts-" + Guid.NewGuid().ToString("N"));
    public InputPathSemanticsTests() => Directory.CreateDirectory(m_root);

    [Fact]
    public void DirectFilesWithoutSearchRootsCaptureComparisonFactsAndRejectAChangedParentPolicy()
    {
        string file = Path.Combine(m_root, "input.h");
        File.WriteAllText(file, "int value(void);");
        HostPathService original = new(_ => new(false), _ => HostIoFailureKind.Permanent);
        SourceSnapshot snapshot = new ExternalInputSnapshotBuilder().Capture([new(file, ExternalInputKind.File)], m_root, original);
        Assert.Empty(snapshot.searchDirectories);
        Assert.Contains(snapshot.pathSemantics, item => item.directory == m_root && !item.caseSensitive);
        HostPathService changed = new(directory => new(directory == m_root), _ => HostIoFailureKind.Permanent);
        Assert.Contains("semantics changed", SourceSnapshotVerifier.GetMismatch(snapshot, changed)!, StringComparison.Ordinal);
        Assert.Equal("int value(void);", File.ReadAllText(file));
    }

    [Fact]
    public void EphemeralOutputObservationsDoNotEnterInputSemanticsIdentity()
    {
        IHostPathService paths = HostTestPaths.Create();
        string file = Path.Combine(m_root, "input.h");
        File.WriteAllText(file, "int value(void);");
        SourceSnapshot first = new ExternalInputSnapshotBuilder().Capture([new(file, ExternalInputKind.File)], m_root, paths);
        string staging = Directory.CreateDirectory(Path.Combine(m_root, ".bgcs-staging-" + Guid.NewGuid().ToString("N"))).FullName;
        _ = paths.GetSemantics(staging);
        SourceSnapshot second = new ExternalInputSnapshotBuilder().Capture([new(file, ExternalInputKind.File)], m_root, paths);
        Assert.Equal(first.pathSemantics.ToArray(), second.pathSemantics.ToArray());
        Assert.DoesNotContain(second.pathSemantics, item => item.directory == staging);
    }

    [Fact]
    public void ExclusionOnlyCandidateMetadataDoesNotMakeTheContentCacheIdentitySessionSpecific()
    {
        IHostPathService paths = HostTestPaths.Create();
        string file = Path.Combine(m_root, "input.h");
        File.WriteAllText(file, "int value(void);");
        SourceSnapshot input = new ExternalInputSnapshotBuilder().Capture([new(file, ExternalInputKind.File)], m_root, paths);
        string firstRoot = Path.Combine(m_root, ".bgcs-staging-first");
        string secondRoot = Path.Combine(m_root, ".bgcs-staging-second");
        SourceSnapshot first = InputPathSemanticsSnapshot.Capture(input with { excludedDirectories = [firstRoot] }, paths);
        SourceSnapshot second = InputPathSemanticsSnapshot.Capture(input with { excludedDirectories = [secondRoot] }, paths);
        Assert.Equal(first.pathSemantics.ToArray(), second.pathSemantics.ToArray());
        Assert.Equal(InputPathSemanticsSnapshot.ForInputIdentity(first).ToArray(), InputPathSemanticsSnapshot.ForInputIdentity(second).ToArray());
        Assert.DoesNotContain(first.pathSemantics, item => item.directory == firstRoot);
        Assert.DoesNotContain(second.pathSemantics, item => item.directory == secondRoot);
        Assert.Null(SourceSnapshotVerifier.GetMismatch(first, paths));
        Assert.Null(SourceSnapshotVerifier.GetMismatch(second, paths));
    }

    [Fact]
    public void ExclusionObservationsStillParticipateInTheLiveHostSemanticsFence()
    {
        string file = Path.Combine(m_root, "input.h");
        File.WriteAllText(file, "int value(void);");
        string candidate = Directory.CreateDirectory(Path.Combine(m_root, "candidate")).FullName;
        bool changed = false;
        HostPathService paths = new(directory => new(changed && directory == candidate), _ => HostIoFailureKind.Permanent);
        SourceSnapshot input = new ExternalInputSnapshotBuilder().Capture([new(file, ExternalInputKind.File)], m_root, paths);
        SourceSnapshot snapshot = InputPathSemanticsSnapshot.Capture(input with { excludedDirectories = [candidate] }, paths);
        _ = paths.GetSemantics(candidate);
        changed = true;
        Assert.DoesNotContain(snapshot.pathSemantics, item => item.directory == candidate);
        Assert.Contains("semantics changed", SourceSnapshotVerifier.GetMismatch(snapshot, paths)!, StringComparison.Ordinal);
    }

    [Fact]
    public void AbsentInputParentsAndTheirAncestorsRemainRequiredComparisonFacts()
    {
        string parent = Directory.CreateDirectory(Path.Combine(m_root, "inputs")).FullName;
        string absent = Path.Combine(parent, "optional.h");
        HostPathService original = new(_ => new(false), _ => HostIoFailureKind.Permanent);
        SourceSnapshot snapshot = new ExternalInputSnapshotBuilder().Capture([new(absent, ExternalInputKind.File, allowMissing: true)], m_root, original);
        Assert.Contains(snapshot.pathSemantics, item => item.directory == parent);
        Assert.Contains(snapshot.pathSemantics, item => item.directory == m_root);
        HostPathService changed = new(directory => new(directory == parent), _ => HostIoFailureKind.Permanent);
        Assert.Contains("semantics changed", SourceSnapshotVerifier.GetMismatch(snapshot, changed)!, StringComparison.Ordinal);
        Assert.False(File.Exists(absent));
    }

    [Fact]
    public void AManualSnapshotCannotOmitItsRequiredInputComparisonFacts()
    {
        IHostPathService paths = HostTestPaths.Create();
        string file = Path.Combine(m_root, "input.h");
        File.WriteAllText(file, "int value(void);");
        SourceSnapshot snapshot = new ExternalInputSnapshotBuilder().Capture([new(file, ExternalInputKind.File)], m_root, paths);
        Assert.Contains("omitted host path semantics", SourceSnapshotVerifier.GetMismatch(snapshot with { pathSemantics = [] }, paths)!, StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentSnapshotJsonRequiresTheComparisonFactsField()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SourceSnapshot>("{\"files\":[],\"searchDirectories\":[]}"));
        SourceSnapshot snapshot = JsonSerializer.Deserialize<SourceSnapshot>("{\"files\":[],\"searchDirectories\":[],\"pathSemantics\":[]}")!;
        Assert.Empty(snapshot.pathSemantics);
    }

    [Theory]
    [InlineData("directory")]
    [InlineData("semantics")]
    [InlineData("snapshot")]
    public void StandalonePublicVerificationStillReobservesChangedHostFacts(string boundary)
    {
        string file = Path.Combine(m_root, "input.h");
        File.WriteAllText(file, "int input(void);");
        bool changed = false;
        HostPathService paths = new(_ => new(changed), _ => HostIoFailureKind.Permanent);
        SourceSnapshot snapshot = new ExternalInputSnapshotBuilder().Capture([new(file, ExternalInputKind.File)], m_root, paths);
        changed = true;
        if (boundary == "directory") Assert.Throws<IOException>(() => DirectoryMembershipDigest.Capture(m_root, paths));
        else if (boundary == "semantics") Assert.Throws<IOException>(() => InputPathSemanticsSnapshot.GetMismatch(snapshot, paths));
        else Assert.Contains("semantics changed", SourceSnapshotVerifier.GetMismatch(snapshot, paths)!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExternalCaptureStillClosesTheFreshHostFenceForFilesAndEmptyInputs(bool empty)
    {
        string file = Path.Combine(m_root, "input.h");
        File.WriteAllText(file, "int input(void);");
        bool changed = false;
        HostPathService paths = new(_ => new(changed), _ => HostIoFailureKind.Permanent);
        _ = paths.GetSemantics(m_root);
        changed = true;
        Assert.Throws<IOException>(() => new ExternalInputSnapshotBuilder().Capture(
            empty ? [] : [new(file, ExternalInputKind.File)], m_root, paths));
    }

    public void Dispose() => Directory.Delete(m_root, recursive: true);
}
