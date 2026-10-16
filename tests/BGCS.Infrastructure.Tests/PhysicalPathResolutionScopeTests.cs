using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Infrastructure.Execution;
using BGCS.Infrastructure.IO;
using Xunit;

namespace BGCS.Infrastructure.Tests;

public sealed class PhysicalPathResolutionScopeTests : IDisposable
{
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
        Scope scope = new();
        Assert.Equal(PhysicalPathResolver.Resolve(path), scope.Resolve(path));
        Assert.Equal(PhysicalPathResolver.Resolve(path), scope.Resolve(path));
        scope.Verify();
        Assert.Throws<InvalidOperationException>(() => scope.Resolve(path));
    }

    [Fact]
    public async Task StableNestedTargetAncestorsAndRepeatedAliasesPreserveTheOrdinaryIdentity()
    {
        string target = Directory.CreateDirectory(Path.Combine(m_root, "target")).FullName;
        Directory.CreateDirectory(Path.Combine(target, "nested"));
        string ancestor = Path.Combine(m_root, "ancestor"), link = Path.Combine(m_root, "link");
        await Link(ancestor, target);
        await Link(link, Path.Combine(ancestor, "nested"));
        Scope scope = new();
        Assert.Equal(PhysicalPathResolver.Resolve(Path.Combine(target, "nested")), scope.Resolve(link));
        Assert.Equal(PhysicalPathResolver.Resolve(Path.Combine(link, "missing")), scope.Resolve(Path.Combine(link, "missing")));
        scope.Verify();
    }

    [Fact]
    public async Task LinkRetargetingCannotReuseAPreviousComponentsObservationAtQueryCompletion()
    {
        var aliases = await PrepareAliases();
        Scope scope = new();
        Assert.Equal(PhysicalPathResolver.Resolve(aliases.first), scope.Resolve(aliases.alias));
        SwitchAlias(aliases.alias, aliases.replacement);
        Assert.Throws<IOException>(scope.Verify);
    }

    [Fact]
    public async Task AnAncestorRedirectIsRecheckedEvenWhenTheResolvedLeafWasMissing()
    {
        var aliases = await PrepareAliases();
        Directory.CreateDirectory(Path.Combine(aliases.first, "nested"));
        Directory.CreateDirectory(Path.Combine(aliases.second, "nested"));
        Scope scope = new();
        string path = Path.Combine(aliases.alias, "nested", "missing.any");
        Assert.Equal(PhysicalPathResolver.Resolve(path), scope.Resolve(path));
        SwitchAlias(aliases.alias, aliases.replacement);
        Assert.Throws<IOException>(scope.Verify);
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
        Scope scope = new();
        Assert.Equal(PhysicalPathResolver.Resolve(link), scope.Resolve(link));
        SwitchAlias(aliases.alias, aliases.replacement);
        Assert.Throws<IOException>(scope.Verify);
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
        Assert.Throws<IOException>(() => DirectoryMembershipDigest.Capture(include, Exclusions()));
    }

    [Fact]
    public void APreviouslyMissingComponentMustStillBeMissingAtQueryCompletion()
    {
        string path = Path.Combine(m_root, "missing", "child.any");
        Scope scope = new();
        Assert.Equal(PhysicalPathResolver.Resolve(path), scope.Resolve(path));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Assert.Throws<IOException>(scope.Verify);
    }

    [Fact]
    public void ReplacingAFileWithADirectoryChangesTheObservedComponentKind()
    {
        string path = Path.Combine(m_root, "kind");
        File.WriteAllText(path, "file");
        Scope scope = new();
        scope.Resolve(path);
        File.Delete(path);
        Directory.CreateDirectory(path);
        Assert.Throws<IOException>(scope.Verify);
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
        Assert.Throws<IOException>(() => DirectoryMembershipDigest.Capture(missing, Exclusions()));
    }

    [Fact]
    public async Task DanglingLinksRetainTheirMissingTargetIdentityAndObserveLaterCreation()
    {
        string target = Directory.CreateDirectory(Path.Combine(m_root, "target")).FullName;
        string alias = Path.Combine(m_root, "dangling");
        await Link(alias, target);
        Directory.Delete(target);
        Scope stable = new();
        Assert.Equal(PhysicalPathResolver.Resolve(alias), stable.Resolve(alias));
        stable.Verify();
        Scope changed = new();
        changed.Resolve(Path.Combine(alias, "missing.any"));
        Directory.CreateDirectory(target);
        Assert.Throws<IOException>(changed.Verify);
    }

    [Fact]
    public async Task ScopeReuseDoesNotReplaceTheOrdinaryActiveCycleChecks()
    {
        string first = Path.Combine(m_root, "first"), second = Path.Combine(m_root, "second");
        Directory.CreateDirectory(second);
        await Link(first, second);
        Directory.Delete(second);
        await Link(second, first);
        Assert.Throws<IOException>(() => PhysicalPathResolver.Resolve(first));
        Scope scope = new();
        Assert.Throws<IOException>(() => scope.Resolve(first));
        Assert.Throws<IOException>(() => scope.Resolve(Path.Combine(second, "tail")));
    }

    [Fact]
    public async Task IndependentQueriesSeeFreshRelationshipsInsteadOfSharingACompletedOrFailedScope()
    {
        var aliases = await PrepareAliases();
        Scope original = new();
        Assert.Equal(PhysicalPathResolver.Resolve(aliases.first), original.Resolve(aliases.alias));
        SwitchAlias(aliases.alias, aliases.replacement);
        Scope current = new();
        Assert.Equal(PhysicalPathResolver.Resolve(aliases.second), current.Resolve(aliases.alias));
        current.Verify();
        Assert.Throws<IOException>(original.Verify);
        Assert.Throws<InvalidOperationException>(() => original.Resolve(aliases.alias));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellationIsObservedDuringResolutionAndDuringTheFinalUncachedReread(bool finalReread)
    {
        Scope scope = new();
        using CancellationTokenSource cancellation = new();
        if (finalReread) scope.Resolve(m_root);
        cancellation.Cancel();
        OperationCanceledException failure = finalReread
            ? Assert.Throws<OperationCanceledException>(() => scope.Verify(cancellation.Token))
            : Assert.Throws<OperationCanceledException>(() => scope.Resolve(m_root, cancellation.Token));
        Assert.Equal(cancellation.Token, failure.CancellationToken);
    }

    [Fact]
    public async Task ConcurrentCapturesHaveIndependentComponentScopes()
    {
        string[] expected = Enumerable.Range(0, 8).Select(index => PhysicalPathResolver.Resolve(Path.Combine(m_root, "missing" + index))).ToArray();
        string[] actual = await Task.WhenAll(expected.Select(path => Task.Run(() =>
        {
            Scope scope = new();
            string result = scope.Resolve(path);
            scope.Verify();
            return result;
        })));
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

    private sealed class Scope
    {
        private static readonly Type s_type = typeof(PhysicalPathResolver).GetNestedType("ResolutionScope", BindingFlags.NonPublic)!;
        private static readonly MethodInfo s_resolve = s_type.GetMethod("Resolve", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private static readonly MethodInfo s_verify = s_type.GetMethod("Verify", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private readonly object m_value = Activator.CreateInstance(s_type, nonPublic: true)!;
        internal string Resolve(string path, CancellationToken cancellationToken = default) => (string)Invoke(s_resolve, [path, cancellationToken])!;
        internal void Verify() => Verify(default);
        internal void Verify(CancellationToken cancellationToken) => Invoke(s_verify, [cancellationToken]);
        private object? Invoke(MethodInfo method, object[] arguments)
        {
            try { return method.Invoke(m_value, arguments); }
            catch (TargetInvocationException failure) when (failure.InnerException is not null)
            { ExceptionDispatchInfo.Capture(failure.InnerException).Throw(); throw; }
        }
    }

    private static void DeleteLink(string path) { if (OperatingSystem.IsWindows()) Directory.Delete(path); else File.Delete(path); }
    public void Dispose()
    {
        for (int index = m_links.Count - 1; index >= 0; index--) DeleteLink(m_links[index]);
        Directory.Delete(m_root, recursive: true);
    }
}
