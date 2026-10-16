using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Infrastructure.Execution;
using BGCS.Infrastructure.IO;
using Xunit;

namespace BGCS.Tests;

public sealed class SourceSnapshotDirectoryBatchTests : IDisposable
{
    private static readonly MethodInfo s_verify = typeof(BindingHost).Assembly.GetType("BGCS.Composition.SourceSnapshotBatch", throwOnError: true)!
        .GetMethod("GetMismatch", BindingFlags.Static | BindingFlags.NonPublic)!;
    private readonly string m_root = Path.Combine(Path.GetTempPath(), "bgcs-directory-fence-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> m_links = [];

    public SourceSnapshotDirectoryBatchTests() => Directory.CreateDirectory(m_root);

    [Fact]
    public void EqualQueriesWithIndependentOrderedExclusionArraysRemainValid()
    {
        string excluded = Directory.CreateDirectory(Path.Combine(m_root, ".private")).FullName;
        string unused = Path.Combine(m_root, "missing-private");
        File.WriteAllText(Path.Combine(m_root, "public.any"), "input");
        DirectoryInputDependency observed = DirectoryMembershipDigest.Capture(m_root, [excluded, unused]);
        SourceSnapshot first = new([], [observed]) { excludedDirectories = ImmutableArray.Create(excluded, unused) };
        SourceSnapshot second = new([], [observed with { }]) { excludedDirectories = ImmutableArray.Create(excluded, unused) };
        Assert.Null(Verify([first, second]));
        // Reordered queries remain separate identities even when exclusions happen to commute.
        SourceSnapshot reordered = second with { excludedDirectories = ImmutableArray.Create(unused, excluded) };
        Assert.Null(Verify([first, reordered]));
    }

    [Theory]
    [InlineData("digest")]
    [InlineData("exists")]
    [InlineData("physical")]
    [InlineData("logical")]
    public void ContradictoryDirectoryRecordsCannotBeHiddenByAnEarlierValidObservation(string difference)
    {
        DirectoryInputDependency actual = DirectoryMembershipDigest.Capture(m_root);
        DirectoryInputDependency wrong = difference switch
        {
            "digest" => actual with { digest = new ContentDigest(new string('0', 64)) },
            "exists" => actual with { exists = !actual.exists },
            "physical" => actual with { resolvedPath = actual.resolvedPath + "-other" },
            _ => actual with { path = Path.Combine(m_root, "other") }
        };
        SourceSnapshot valid = new([], [actual]), invalid = new([], [wrong]);
        Assert.NotNull(Verify([valid, invalid]));
        Assert.NotNull(Verify([invalid, valid]));
    }

    [Fact]
    public void ExclusionsArePartOfTheQueryEvenForAnIdenticalRecordedDirectoryDigest()
    {
        string excluded = Directory.CreateDirectory(Path.Combine(m_root, ".private")).FullName;
        File.WriteAllText(Path.Combine(excluded, "candidate.any"), "private");
        DirectoryInputDependency actual = DirectoryMembershipDigest.Capture(m_root, [excluded]);
        SourceSnapshot first = new([], [actual]) { excludedDirectories = [excluded] };
        SourceSnapshot conflicting = first with { excludedDirectories = [] };
        Assert.Null(Verify([first, first with { excludedDirectories = [excluded] }]));
        Assert.NotNull(Verify([first, conflicting]));
    }

    [Fact]
    public void ANewFenceRechecksMembershipAfterAnArbitraryExtensionIsAdded()
    {
        SourceSnapshot observed = new([], [DirectoryMembershipDigest.Capture(m_root)]);
        Assert.Null(Verify([observed, observed]));
        File.WriteAllText(Path.Combine(m_root, "added.unrecognized-extension"), "new candidate");
        Assert.NotNull(Verify([observed, observed]));
        SourceSnapshot refreshed = new([], [DirectoryMembershipDigest.Capture(m_root)]);
        Assert.Null(Verify([refreshed, refreshed]));
    }

    [Fact]
    public void DuplicateDirectoriesDoNotSuppressASecondSnapshotsAbsentInputs()
    {
        string missing = Path.Combine(m_root, "later.any");
        SourceSnapshot first = new([], [DirectoryMembershipDigest.Capture(m_root)]);
        SourceSnapshot second = first with { absentInputs = [new(missing, PhysicalPathResolver.Resolve(missing))] };
        Assert.Null(Verify([first, second]));
        // Keep membership valid independently, isolating the absent-input obligation.
        File.WriteAllText(missing, "now present");
        DirectoryInputDependency current = DirectoryMembershipDigest.Capture(m_root);
        Assert.NotNull(Verify([first with { searchDirectories = [current] }, second with { searchDirectories = [current] }]));
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
        SourceSnapshot valid = new([], [DirectoryMembershipDigest.Capture(m_root)]);
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
        Assert.Contains("uninitialized or null", Verify([valid, invalid]));
    }

    [Fact]
    public void CancellationRemainsObservableForAnOtherwiseDuplicateBatch()
    {
        SourceSnapshot snapshot = new([], [DirectoryMembershipDigest.Capture(m_root)]);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => Verify([snapshot, snapshot], cancellation.Token));
    }

    [Fact]
    public async Task OutsideRootExclusionsStillAffectAnEscapingDirectoryLink()
    {
        string include = Directory.CreateDirectory(Path.Combine(m_root, "include")).FullName;
        string outside = Directory.CreateDirectory(Path.Combine(m_root, "outside")).FullName;
        File.WriteAllText(Path.Combine(outside, "hidden.any"), "external");
        await Link(Path.Combine(include, "escape"), outside);
        DirectoryInputDependency observed = DirectoryMembershipDigest.Capture(include, [outside]);
        SourceSnapshot excluded = new([], [observed]) { excludedDirectories = [outside] };
        Assert.Null(Verify([excluded, excluded]));
        Assert.NotNull(Verify([excluded, excluded with { excludedDirectories = [] }]));
    }

    [Fact]
    public async Task ANewFenceRechecksTheSearchRootsPhysicalIdentityAfterRetargeting()
    {
        string first = Directory.CreateDirectory(Path.Combine(m_root, "first")).FullName;
        string second = Directory.CreateDirectory(Path.Combine(m_root, "second")).FullName;
        string alias = Path.Combine(m_root, "alias");
        await Link(alias, first);
        SourceSnapshot snapshot = new([], [DirectoryMembershipDigest.Capture(alias)]);
        Assert.Null(Verify([snapshot, snapshot]));
        DeleteLink(alias); m_links.Remove(alias);
        await Link(alias, second);
        Assert.NotNull(Verify([snapshot, snapshot]));
    }

    private static string? Verify(IEnumerable<SourceSnapshot> snapshots, CancellationToken cancellationToken = default)
    {
        try { return (string?)s_verify.Invoke(null, [snapshots, cancellationToken]); }
        catch (TargetInvocationException failure) when (failure.InnerException is not null)
        { ExceptionDispatchInfo.Capture(failure.InnerException).Throw(); throw; }
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
