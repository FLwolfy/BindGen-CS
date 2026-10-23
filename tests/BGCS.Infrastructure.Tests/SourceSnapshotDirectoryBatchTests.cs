using BGCS.Core.Hosting;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Infrastructure.Execution;
using BGCS.Infrastructure.IO;
using Xunit;

namespace BGCS.Infrastructure.Tests;

public sealed class SourceSnapshotDirectoryBatchTests : IDisposable
{
    private readonly IHostPathService m_paths = HostTestPaths.Create();
    private readonly string m_root = Path.Combine(Path.GetTempPath(), "bgcs-directory-fence-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> m_links = [];

    public SourceSnapshotDirectoryBatchTests() => Directory.CreateDirectory(m_root);

    [Fact]
    public void EqualQueriesWithIndependentOrderedExclusionArraysRemainValid()
    {
        string excluded = Directory.CreateDirectory(Path.Combine(m_root, ".private")).FullName;
        string unused = Path.Combine(m_root, "missing-private");
        File.WriteAllText(Path.Combine(m_root, "public.any"), "input");
        DirectoryInputDependency observed = DirectoryMembershipDigest.Capture(m_root, m_paths, [excluded, unused]);
        SourceSnapshot first = new([], [observed]) { excludedDirectories = ImmutableArray.Create(excluded, unused) };
        first = InputPathSemanticsSnapshot.Capture(first, m_paths);
        SourceSnapshot second = new([], [observed with { }]) { excludedDirectories = ImmutableArray.Create(excluded, unused) };
        second = InputPathSemanticsSnapshot.Capture(second, m_paths);
        Assert.Null(SourceSnapshotBatch.GetMismatch([first, second], m_paths));
        // Reordered queries remain separate identities even when exclusions happen to commute.
        SourceSnapshot reordered = second with { excludedDirectories = ImmutableArray.Create(unused, excluded) };
        Assert.Null(SourceSnapshotBatch.GetMismatch([first, reordered], m_paths));
    }

    [Theory]
    [InlineData("digest")]
    [InlineData("exists")]
    [InlineData("physical")]
    [InlineData("logical")]
    public void ContradictoryDirectoryRecordsCannotBeHiddenByAnEarlierValidObservation(string difference)
    {
        DirectoryInputDependency actual = DirectoryMembershipDigest.Capture(m_root, m_paths);
        DirectoryInputDependency wrong = difference switch
        {
            "digest" => actual with { digest = new ContentDigest(new string('0', 64)) },
            "exists" => actual with { exists = !actual.exists },
            "physical" => actual with { resolvedPath = actual.resolvedPath + "-other" },
            _ => actual with { path = Path.Combine(m_root, "other") }
        };
        SourceSnapshot valid = new([], [actual]), invalid = new([], [wrong]);
        valid = InputPathSemanticsSnapshot.Capture(valid, m_paths);
        invalid = InputPathSemanticsSnapshot.Capture(invalid, m_paths);
        Assert.NotNull(SourceSnapshotBatch.GetMismatch([valid, invalid], m_paths));
        Assert.NotNull(SourceSnapshotBatch.GetMismatch([invalid, valid], m_paths));
    }

    [Fact]
    public void ExclusionsArePartOfTheQueryEvenForAnIdenticalRecordedDirectoryDigest()
    {
        string excluded = Directory.CreateDirectory(Path.Combine(m_root, ".private")).FullName;
        File.WriteAllText(Path.Combine(excluded, "candidate.any"), "private");
        DirectoryInputDependency actual = DirectoryMembershipDigest.Capture(m_root, m_paths, [excluded]);
        SourceSnapshot first = new([], [actual]) { excludedDirectories = [excluded] };
        first = InputPathSemanticsSnapshot.Capture(first, m_paths);
        SourceSnapshot conflicting = first with { excludedDirectories = [] };
        Assert.Null(SourceSnapshotBatch.GetMismatch([first, first with { excludedDirectories = [excluded] }], m_paths));
        Assert.NotNull(SourceSnapshotBatch.GetMismatch([first, conflicting], m_paths));
    }

    [Fact]
    public void ANewFenceRechecksMembershipAfterAnArbitraryExtensionIsAdded()
    {
        SourceSnapshot observed = new([], [DirectoryMembershipDigest.Capture(m_root, m_paths)]);
        observed = InputPathSemanticsSnapshot.Capture(observed, m_paths);
        Assert.Null(SourceSnapshotBatch.GetMismatch([observed, observed], m_paths));
        File.WriteAllText(Path.Combine(m_root, "added.unrecognized-extension"), "new candidate");
        Assert.NotNull(SourceSnapshotBatch.GetMismatch([observed, observed], m_paths));
        SourceSnapshot refreshed = new([], [DirectoryMembershipDigest.Capture(m_root, m_paths)]);
        refreshed = InputPathSemanticsSnapshot.Capture(refreshed, m_paths);
        Assert.Null(SourceSnapshotBatch.GetMismatch([refreshed, refreshed], m_paths));
    }

    [Fact]
    public void DuplicateDirectoriesDoNotSuppressASecondSnapshotsAbsentInputs()
    {
        string missing = Path.Combine(m_root, "later.any");
        SourceSnapshot first = new([], [DirectoryMembershipDigest.Capture(m_root, m_paths)]);
        first = InputPathSemanticsSnapshot.Capture(first, m_paths);
        SourceSnapshot second = first with { absentInputs = [new(missing, PhysicalPathResolver.Resolve(missing, m_paths))] };
        Assert.Null(SourceSnapshotBatch.GetMismatch([first, second], m_paths));
        // Keep membership valid independently, isolating the absent-input obligation.
        File.WriteAllText(missing, "now present");
        DirectoryInputDependency current = DirectoryMembershipDigest.Capture(m_root, m_paths);
        Assert.NotNull(SourceSnapshotBatch.GetMismatch([first with { searchDirectories = [current] }, second with { searchDirectories = [current] }], m_paths));
    }

    [Theory]
    [InlineData("files")]
    [InlineData("directories")]
    [InlineData("exclusions")]
    [InlineData("absence")]
    [InlineData("null-file")]
    [InlineData("null-directory")]
    [InlineData("null-absence")]
    public void EarlierEquivalentQueriesCannotMaskMalformedOriginalCollections(string collection)
    {
        SourceSnapshot valid = new([], [DirectoryMembershipDigest.Capture(m_root, m_paths)]);
        valid = InputPathSemanticsSnapshot.Capture(valid, m_paths);
        SourceSnapshot invalid = collection switch
        {
            "files" => valid with { files = default },
            "directories" => valid with { searchDirectories = default },
            "exclusions" => valid with { excludedDirectories = default },
            "absence" => valid with { absentInputs = default },
            "null-file" => valid with { files = [null!] },
            "null-directory" => valid with { searchDirectories = [null!] },
            _ => valid with { absentInputs = [null!] }
        };
        Assert.Contains("uninitialized or null", SourceSnapshotBatch.GetMismatch([valid, invalid], m_paths));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong")]
    [InlineData("null")]
    public void RepeatedFilesCannotHideMalformedOrContradictoryOriginalComparisonFacts(string change)
    {
        string file = Path.Combine(m_root, "input.h");
        File.WriteAllText(file, "int input(void);");
        SourceSnapshot valid = new ExternalInputSnapshotBuilder().Capture([new(file, ExternalInputKind.File)], m_root, m_paths);
        SourceSnapshot invalid = valid with
        {
            pathSemantics = change switch
            {
                "missing" => [],
                "wrong" => valid.pathSemantics.Select(item => item.directory == m_root ? item with { caseSensitive = !item.caseSensitive } : item).ToImmutableArray(),
                _ => [null!]
            }
        };
        string expected = change == "missing" ? "omitted host path semantics" : change == "wrong" ? "semantics changed" : "invalid host path semantics";
        Assert.Contains(expected, SourceSnapshotBatch.GetMismatch([valid, invalid], m_paths)!, StringComparison.Ordinal);
    }

    [Fact]
    public void CancellationRemainsObservableForAnOtherwiseDuplicateBatch()
    {
        SourceSnapshot snapshot = new([], [DirectoryMembershipDigest.Capture(m_root, m_paths)]);
        snapshot = InputPathSemanticsSnapshot.Capture(snapshot, m_paths);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => SourceSnapshotBatch.GetMismatch([snapshot, snapshot], m_paths, cancellation.Token));
    }

    [Fact]
    public async Task OutsideRootExclusionsStillAffectAnEscapingDirectoryLink()
    {
        string include = Directory.CreateDirectory(Path.Combine(m_root, "include")).FullName;
        string outside = Directory.CreateDirectory(Path.Combine(m_root, "outside")).FullName;
        File.WriteAllText(Path.Combine(outside, "hidden.any"), "external");
        await Link(Path.Combine(include, "escape"), outside);
        DirectoryInputDependency observed = DirectoryMembershipDigest.Capture(include, m_paths, [outside]);
        SourceSnapshot excluded = new([], [observed]) { excludedDirectories = [outside] };
        excluded = InputPathSemanticsSnapshot.Capture(excluded, m_paths);
        Assert.Null(SourceSnapshotBatch.GetMismatch([excluded, excluded], m_paths));
        Assert.NotNull(SourceSnapshotBatch.GetMismatch([excluded, excluded with { excludedDirectories = [] }], m_paths));
    }

    [Fact]
    public async Task ANewFenceRechecksTheSearchRootsPhysicalIdentityAfterRetargeting()
    {
        string first = Directory.CreateDirectory(Path.Combine(m_root, "first")).FullName;
        string second = Directory.CreateDirectory(Path.Combine(m_root, "second")).FullName;
        string alias = Path.Combine(m_root, "alias");
        await Link(alias, first);
        SourceSnapshot snapshot = new([], [DirectoryMembershipDigest.Capture(alias, m_paths)]);
        snapshot = InputPathSemanticsSnapshot.Capture(snapshot, m_paths);
        Assert.Null(SourceSnapshotBatch.GetMismatch([snapshot, snapshot], m_paths));
        DeleteLink(alias); m_links.Remove(alias);
        await Link(alias, second);
        Assert.NotNull(SourceSnapshotBatch.GetMismatch([snapshot, snapshot], m_paths));
    }

    [Fact]
    public async Task AFileLinkRetargetedBeforeAnEmptyLaterSnapshotFailsTheOuterFence()
    {
        string first = Directory.CreateDirectory(Path.Combine(m_root, "first")).FullName;
        string second = Directory.CreateDirectory(Path.Combine(m_root, "second")).FullName;
        foreach (string directory in new[] { first, second }) File.WriteAllText(Path.Combine(directory, "value.h"), "identical bytes");
        string alias = Path.Combine(m_root, "alias");
        string replacement = Path.Combine(m_root, "replacement");
        await Link(alias, first);
        await Link(replacement, second);
        SourceSnapshot snapshot = new ExternalInputSnapshotBuilder().Capture(
            [new(Path.Combine(alias, "value.h"), ExternalInputKind.File)], m_root, m_paths);
        string? mismatch = SourceSnapshotBatch.GetMismatch(Snapshots(), m_paths);
        Assert.Contains("path component changed", mismatch!, StringComparison.Ordinal);

        IEnumerable<SourceSnapshot> Snapshots()
        {
            yield return snapshot;
            DeleteLink(alias); m_links.Remove(alias);
            Directory.Move(replacement, alias);
            m_links.Remove(replacement); m_links.Add(alias);
            yield return new([], []);
        }
    }

    private async Task Link(string path, string target)
    {
        if (OperatingSystem.IsWindows())
        {
            string executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            string command = "$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path '" + path.Replace("'", "''", StringComparison.Ordinal)
                + "' -Target '" + target.Replace("'", "''", StringComparison.Ordinal) + "' | Out-Null";
            var result = await ProcessExecutor.ExecuteAsync(executable,
                ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(command))], m_root, TimeSpan.FromSeconds(30));
            if (new DirectoryInfo(path).LinkTarget is not null) m_links.Add(path);
            Assert.True(result.exitCode == 0, result.standardOutput + Environment.NewLine + result.standardError);
        }
        else { Directory.CreateSymbolicLink(path, target); m_links.Add(path); }
    }

    private static void DeleteLink(string path) { if (OperatingSystem.IsWindows()) Directory.Delete(path); else File.Delete(path); }
    public void Dispose()
    {
        for (int index = m_links.Count - 1; index >= 0; index--) DeleteLink(m_links[index]);
        Directory.Delete(m_root, recursive: true);
    }
}
