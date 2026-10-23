using BGCS.Core.Hosting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Infrastructure.Execution;
using BGCS.Infrastructure.IO;
using Xunit;

namespace BGCS.Infrastructure.Tests;

public sealed class PhysicalPathResolutionScopeTests : IDisposable
{
    private readonly IHostPathService m_paths = HostTestPaths.Create();
    private readonly string m_root = Path.Combine(Path.GetTempPath(), "bgcs-component-scope-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> m_links = [];

    public PhysicalPathResolutionScopeTests() => Directory.CreateDirectory(m_root);

    [Theory]
    [InlineData("directory")]
    [InlineData("file")]
    [InlineData("missing")]
    [InlineData("missing-tail")]
    [InlineData("unicode")]
    public void ScopedAndOrdinaryResolutionKeepStableExistingAndMissingIdentities(string kind)
    {
        string path = Path.Combine(m_root, kind == "unicode" ? "λ space" : kind);
        if (kind is "directory" or "unicode") Directory.CreateDirectory(path);
        if (kind == "file") File.WriteAllText(path, "original");
        if (kind == "missing-tail") path = Path.Combine(path, "nested", "file.any");
        var first = DirectoryMembershipDigest.Capture(path, m_paths);
        Assert.Equal(PhysicalPathResolver.Resolve(path, m_paths), first.resolvedPath);
        Assert.Equal(first, DirectoryMembershipDigest.Capture(path, m_paths));
    }

    [Fact]
    public async Task StableNestedTargetAncestorsAndRepeatedAliasesPreserveTheOrdinaryIdentity()
    {
        string target = Directory.CreateDirectory(Path.Combine(m_root, "target")).FullName;
        Directory.CreateDirectory(Path.Combine(target, "nested"));
        string ancestor = Path.Combine(m_root, "ancestor"), link = Path.Combine(m_root, "link");
        await Link(ancestor, target);
        await Link(link, Path.Combine(ancestor, "nested"));
        var captured = DirectoryMembershipDigest.Capture(link, m_paths);
        Assert.Equal(PhysicalPathResolver.Resolve(Path.Combine(target, "nested"), m_paths), captured.resolvedPath);
        Assert.Equal(captured, DirectoryMembershipDigest.Capture(link, m_paths));
    }

    [Fact]
    public async Task LinkRetargetingCannotReuseAPreviousComponentsObservationAtQueryCompletion()
    {
        var aliases = await PrepareAliases();
        Assert.Throws<IOException>(() => CaptureWhile(aliases.alias, () => SwitchAlias(aliases.alias, aliases.replacement)));
    }

    [Fact]
    public async Task AnAncestorRedirectIsRecheckedEvenWhenTheResolvedLeafWasMissing()
    {
        var aliases = await PrepareAliases();
        Directory.CreateDirectory(Path.Combine(aliases.first, "nested"));
        Directory.CreateDirectory(Path.Combine(aliases.second, "nested"));
        string path = Path.Combine(aliases.alias, "nested", "missing.any");
        Assert.Throws<IOException>(() => CaptureWhile(path, () => SwitchAlias(aliases.alias, aliases.replacement)));
    }

    [Fact]
    public async Task ALinkTargetsOwnOutsideRootAncestorIsIncludedInFinalRevalidation()
    {
        var aliases = await PrepareAliases();
        Directory.CreateDirectory(Path.Combine(aliases.first, "nested"));
        Directory.CreateDirectory(Path.Combine(aliases.second, "nested"));
        string include = Directory.CreateDirectory(Path.Combine(m_root, "include")).FullName;
        string link = Path.Combine(include, "external");
        await Link(link, Path.Combine(aliases.alias, "nested"));
        Assert.Throws<IOException>(() => CaptureWhile(link, () => SwitchAlias(aliases.alias, aliases.replacement)));
    }

    [Fact]
    public async Task ExclusionRelationshipsOutsideTheSearchRootAreRevalidatedByTheCompleteCapture()
    {
        var aliases = await PrepareAliases();
        string include = Directory.CreateDirectory(Path.Combine(m_root, "include")).FullName;
        File.WriteAllText(Path.Combine(include, "public.any"), "original");
        IEnumerable<string> Exclusions()
        {
            yield return aliases.alias;
            SwitchAlias(aliases.alias, aliases.replacement);
        }
        Assert.Throws<IOException>(() => DirectoryMembershipDigest.Capture(include, m_paths, Exclusions()));
    }

    [Fact]
    public void APreviouslyMissingComponentMustStillBeMissingAtQueryCompletion()
    {
        string path = Path.Combine(m_root, "missing", "child.any");
        Assert.Throws<IOException>(() => CaptureWhile(path, () => Directory.CreateDirectory(Path.GetDirectoryName(path)!)));
    }

    [Fact]
    public void ReplacingAFileWithADirectoryChangesTheObservedComponentKind()
    {
        string path = Path.Combine(m_root, "kind");
        File.WriteAllText(path, "file");
        Assert.Throws<IOException>(() => CaptureWhile(path, () => { File.Delete(path); Directory.CreateDirectory(path); }));
    }

    [Fact]
    public void AMissingRootCreatedWhileExclusionsAreEnumeratedCannotReturnAStaleMissingDigest()
    {
        string missing = Path.Combine(m_root, "missing-root");
        IEnumerable<string> Exclusions()
        {
            Directory.CreateDirectory(missing);
            yield return Path.Combine(m_root, "private");
        }
        Assert.Throws<IOException>(() => DirectoryMembershipDigest.Capture(missing, m_paths, Exclusions()));
    }

    [Fact]
    public async Task DanglingLinksRetainTheirMissingTargetIdentityAndObserveLaterCreation()
    {
        string target = Directory.CreateDirectory(Path.Combine(m_root, "target")).FullName;
        string alias = Path.Combine(m_root, "dangling");
        await Link(alias, target);
        Directory.Delete(target);
        Assert.Equal(PhysicalPathResolver.Resolve(alias, m_paths), DirectoryMembershipDigest.Capture(alias, m_paths).resolvedPath);
        Assert.Throws<IOException>(() => CaptureWhile(Path.Combine(alias, "missing.any"), () => Directory.CreateDirectory(target)));
    }

    [Fact]
    public async Task ScopeReuseDoesNotReplaceTheOrdinaryActiveCycleChecks()
    {
        string first = Path.Combine(m_root, "first"), second = Path.Combine(m_root, "second");
        Directory.CreateDirectory(second);
        await Link(first, second);
        Directory.Delete(second);
        await Link(second, first);
        Assert.Throws<IOException>(() => PhysicalPathResolver.Resolve(first, m_paths));
        Assert.Throws<IOException>(() => DirectoryMembershipDigest.Capture(first, m_paths));
        Assert.Throws<IOException>(() => DirectoryMembershipDigest.Capture(Path.Combine(second, "tail"), m_paths));
    }

    [Fact]
    public async Task IndependentQueriesSeeFreshRelationshipsInsteadOfSharingACompletedOrFailedScope()
    {
        var aliases = await PrepareAliases();
        var original = DirectoryMembershipDigest.Capture(aliases.alias, m_paths);
        Assert.Equal(PhysicalPathResolver.Resolve(aliases.first, m_paths), original.resolvedPath);
        Assert.Throws<IOException>(() => CaptureWhile(aliases.alias, () => SwitchAlias(aliases.alias, aliases.replacement)));
        var current = DirectoryMembershipDigest.Capture(aliases.alias, m_paths);
        Assert.Equal(PhysicalPathResolver.Resolve(aliases.second, m_paths), current.resolvedPath);
        Assert.NotEqual(original, current);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellationIsObservedDuringResolutionAndDuringTheFinalUncachedReread(bool finalReread)
    {
        using CancellationTokenSource cancellation = new();
        IEnumerable<string> CancelBeforeFinalFence()
        {
            cancellation.Cancel();
            yield break;
        }
        if (!finalReread) cancellation.Cancel();
        OperationCanceledException failure = Assert.Throws<OperationCanceledException>(() =>
            DirectoryMembershipDigest.Capture(m_root, m_paths, finalReread ? CancelBeforeFinalFence() : [], cancellation.Token));
        Assert.Equal(cancellation.Token, failure.CancellationToken);
    }

    [Fact]
    public async Task ConcurrentCapturesHaveIndependentComponentScopes()
    {
        string[] expected = Enumerable.Range(0, 8).Select(index => PhysicalPathResolver.Resolve(Path.Combine(m_root, "missing" + index), m_paths)).ToArray();
        string[] actual = await Task.WhenAll(expected.Select(path => Task.Run(() => DirectoryMembershipDigest.Capture(path, m_paths).resolvedPath)));
        Assert.Equal(expected, actual);
    }

    private async Task<(string alias, string replacement, string first, string second)> PrepareAliases()
    {
        string first = Directory.CreateDirectory(Path.Combine(m_root, "first")).FullName;
        string second = Directory.CreateDirectory(Path.Combine(m_root, "second")).FullName;
        string alias = Path.Combine(m_root, "alias"), replacement = Path.Combine(m_root, "replacement");
        await Link(alias, first);
        await Link(replacement, second);
        return (alias, replacement, first, second);
    }

    private void SwitchAlias(string alias, string replacement)
    {
        DeleteLink(alias);
        Directory.Move(replacement, alias);
        m_links.Remove(replacement);
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

    private BGCS.Core.Inputs.DirectoryInputDependency CaptureWhile(string directory, Action change)
    {
        IEnumerable<string> Exclusions()
        {
            change();
            yield break;
        }
        return DirectoryMembershipDigest.Capture(directory, m_paths, Exclusions());
    }

    private static void DeleteLink(string path) { if (OperatingSystem.IsWindows()) Directory.Delete(path); else File.Delete(path); }
    public void Dispose()
    {
        for (int index = m_links.Count - 1; index >= 0; index--) DeleteLink(m_links[index]);
        Directory.Delete(m_root, recursive: true);
    }
}
