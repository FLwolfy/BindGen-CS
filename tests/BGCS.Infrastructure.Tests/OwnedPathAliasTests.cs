using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Infrastructure.Caching;
using BGCS.Infrastructure.Execution;
using BGCS.Infrastructure.IO;
using BGCS.Infrastructure.Publishing;
using BGCS.Infrastructure.Verification;
using Xunit;

namespace BGCS.Infrastructure.Tests;

public sealed class OwnedPathAliasTests : IDisposable
{
    // Keep the actual host spelling (including any OS parent alias) at every public entry point.
    private readonly string m_root = Path.Combine(Path.GetTempPath(), "bgcs-owned-alias-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> m_links = [];
    private readonly string m_target;
    private readonly string m_other;
    private readonly string m_alias;
    private static readonly ArtifactDescriptor s_artifact = Descriptor("value.txt", "original");

    public OwnedPathAliasTests()
    {
        m_target = Path.Combine(m_root, "target");
        m_other = Path.Combine(m_root, "other");
        m_alias = Path.Combine(m_root, "alias");
        Directory.CreateDirectory(m_target);
        Directory.CreateDirectory(m_other);
    }

    [Theory]
    [InlineData("reader")]
    [InlineData("sink")]
    [InlineData("editor")]
    [InlineData("verifier")]
    [InlineData("cache")]
    [InlineData("publisher")]
    [InlineData("publication-reader")]
    [InlineData("transaction")]
    [InlineData("transaction-set")]
    public async Task ExplicitParentAliasSupportsEachOwnedSurface(string surface)
    {
        await Link(m_alias, m_target);
        string logical = Path.Combine(m_alias, "owned"), physical = Path.Combine(m_target, "owned");
        if (surface is "reader" or "editor" or "verifier")
        {
            Directory.CreateDirectory(physical);
            File.WriteAllText(Path.Combine(physical, "value.txt"), "original", new UTF8Encoding(false));
        }
        switch (surface)
        {
            case "reader":
                Assert.Equal("original", await new DirectoryArtifactReader(logical, [s_artifact]).ReadTextAsync("value.txt", default));
                break;
            case "sink":
                new DirectoryArtifactSink(logical).WriteText("value.txt", "original");
                break;
            case "editor":
                using (CandidateTextEditor editor = new(logical, [s_artifact], "extra/"))
                {
                    await editor.ReplaceTextAsync("value.txt", "updated", default);
                    await editor.AddTextAsync("extra/new.txt", "text/plain", "new", default);
                    Assert.Equal("updated", await editor.ReadTextAsync("value.txt", default));
                    await editor.DeleteTextAsync("extra/new.txt", default);
                    Assert.Single(editor.artifacts);
                }
                break;
            case "verifier": new ArtifactDigestVerifier().Verify(logical, [s_artifact]); break;
            case "cache":
                GenerationStageCache cache = new(logical);
                cache.Prepare();
                ContentDigest key = Digest("key");
                Assert.True(cache.Store(key, [new(s_artifact, Encoding.UTF8.GetBytes("original"))]));
                Assert.True(cache.TryRead(key, out var cached, out string? reason));
                Assert.Null(reason);
                Assert.Equal("original", Encoding.UTF8.GetString(Assert.Single(cached).content.Span));
                break;
            case "publisher": Publish(logical, "original"); break;
            case "publication-reader":
                Publish(logical, "original");
                Assert.Equal(s_artifact, Assert.Single(new ArtifactPublicationReader().ReadVerified(logical, "fixture")));
                break;
            case "transaction":
                using (OutputDirectoryTransaction transaction = new(logical))
                {
                    File.WriteAllText(Path.Combine(transaction.stagingPath, "value.txt"), "original");
                    transaction.Commit();
                }
                break;
            case "transaction-set":
                using (OutputDirectorySetTransaction transactions = new([logical]))
                {
                    File.WriteAllText(Path.Combine(transactions.GetStagingPath(logical), "value.txt"), "original");
                    transactions.Commit();
                }
                break;
        }
        if (surface != "cache") Assert.Equal(surface == "editor" ? "updated" : "original", File.ReadAllText(Path.Combine(physical, "value.txt")));
    }

    [Theory]
    [InlineData("reader")]
    [InlineData("sink")]
    [InlineData("editor")]
    [InlineData("cache")]
    [InlineData("publisher")]
    [InlineData("transaction")]
    [InlineData("transaction-set")]
    public async Task RedirectingCapturedParentNeverGrantsOwnershipOfTheNewTarget(string surface)
    {
        await Link(m_alias, m_target);
        string logical = Path.Combine(m_alias, "owned"), physical = Path.Combine(m_target, "owned");
        Publish(physical, "original");
        Func<Task> operation;
        IDisposable? owner = null;
        switch (surface)
        {
            case "reader":
                DirectoryArtifactReader reader = new(logical, [s_artifact]);
                operation = async () => { _ = await reader.ReadTextAsync("value.txt", default); };
                break;
            case "sink":
                DirectoryArtifactSink sink = new(logical);
                operation = () => { sink.WriteText("new.txt", "unsafe"); return Task.CompletedTask; };
                break;
            case "editor":
                CandidateTextEditor editor = new(logical, [s_artifact]); owner = editor;
                operation = async () => { await editor.ReplaceTextAsync("value.txt", "unsafe", default); };
                break;
            case "cache":
                GenerationStageCache cache = new(logical);
                operation = () => { cache.Store(Digest("key"), [new(s_artifact, Encoding.UTF8.GetBytes("original"))]); return Task.CompletedTask; };
                break;
            case "publisher":
                ArtifactPublisher publisher = new(logical, "fixture"); owner = publisher;
                new DirectoryArtifactSink(publisher.candidateRoot).WriteText("value.txt", "unsafe");
                operation = () => { publisher.Commit([Descriptor("value.txt", "unsafe")]); return Task.CompletedTask; };
                break;
            case "transaction":
                OutputDirectoryTransaction transaction = new(logical); owner = transaction;
                File.WriteAllText(Path.Combine(transaction.stagingPath, "value.txt"), "unsafe");
                operation = () => { transaction.Commit(); return Task.CompletedTask; };
                break;
            default:
                OutputDirectorySetTransaction transactions = new([logical]); owner = transactions;
                File.WriteAllText(Path.Combine(transactions.GetStagingPath(logical), "value.txt"), "unsafe");
                operation = () => { transactions.Commit(); return Task.CompletedTask; };
                break;
        }
        try
        {
            DeleteLink(m_alias);
            m_links.Remove(m_alias);
            await Link(m_alias, m_other);
            await Assert.ThrowsAsync<IOException>(operation);
            Assert.Equal("original", File.ReadAllText(Path.Combine(physical, "value.txt")));
            Assert.Empty(Directory.EnumerateFileSystemEntries(m_other));
        }
        finally { owner?.Dispose(); }
    }

    [Fact]
    public async Task NestedAbsoluteAndRelativeTargetsResolveTheirOwnAncestorAliasesAndMissingSuffixes()
    {
        await Link(m_alias, m_target);
        Directory.CreateDirectory(Path.Combine(m_target, "nested"));
        string second = Path.Combine(m_root, "second");
        await Link(second, Path.Combine(m_alias, "nested"));
        Assert.Equal(PhysicalPathResolver.Resolve(Path.Combine(m_target, "nested", "absent", "file.h")),
            PhysicalPathResolver.Resolve(Path.Combine(second, "absent", "file.h")));
        if (!OperatingSystem.IsWindows())
        {
            string relative = Path.Combine(m_root, "relative");
            await Link(relative, Path.Combine("alias", "nested"));
            Assert.Equal(PhysicalPathResolver.Resolve(Path.Combine(m_target, "nested")), PhysicalPathResolver.Resolve(relative));
        }
    }

    [Fact]
    public async Task PhysicalOwnerDeterminesLocksDuplicateDestinationsAndProtectedOverlaps()
    {
        await Link(m_alias, m_target);
        string logical = Path.Combine(m_alias, "owned"), physical = Path.Combine(m_target, "owned");
        using (OutputDirectoryTransaction first = new(logical))
            Assert.Throws<TimeoutException>(() => new OutputDirectoryTransaction(physical, TimeSpan.FromMilliseconds(60)));
        Assert.Throws<ArgumentException>(() => new OutputDirectorySetTransaction([logical, physical]));
        Assert.Throws<IOException>(() => new ArtifactPublisher(logical, "fixture", [Path.Combine(physical, "source.h")]));
    }

    [Fact]
    public async Task LinkedOwnedLeavesAndEscapingDescendantsRemainForbidden()
    {
        await Link(m_alias, m_target);
        Assert.Throws<IOException>(() => new DirectoryArtifactReader(m_alias, []));
        Assert.Throws<IOException>(() => new DirectoryArtifactSink(m_alias));
        Assert.Throws<IOException>(() => new CandidateTextEditor(m_alias, []));
        Assert.Throws<IOException>(() => new ArtifactDigestVerifier().Verify(m_alias, []));
        Assert.Throws<IOException>(() => new GenerationStageCache(m_alias));
        Assert.Throws<IOException>(() => new ArtifactPublisher(m_alias, "fixture"));
        Assert.Throws<IOException>(() => new OutputDirectoryTransaction(m_alias));
        string owned = Path.Combine(m_target, "owned");
        Publish(owned, "original");
        File.WriteAllText(Path.Combine(m_other, "secret.txt"), "secret");
        await Link(Path.Combine(owned, "escape"), m_other);
        Assert.Throws<IOException>(() => new DirectoryArtifactReader(owned, [Descriptor("escape/secret.txt", "secret")]));
        Assert.Throws<IOException>(() => new DirectoryArtifactSink(owned).WriteText("escape/new.txt", "unsafe"));
        Assert.Throws<IOException>(() => new ArtifactDigestVerifier().Verify(owned, [s_artifact], [".bgcs-artifacts.json"]));
        Assert.Throws<IOException>(() => new ArtifactPublisher(owned, "fixture"));
        Assert.False(File.Exists(Path.Combine(m_other, "new.txt")));
        Assert.Equal("secret", File.ReadAllText(Path.Combine(m_other, "secret.txt")));
    }

    [Fact]
    public async Task DanglingLeafAndCyclesRemainRejectedWithoutCreatingTargets()
    {
        string missing = Path.Combine(m_root, "missing");
        Directory.CreateDirectory(missing);
        await Link(m_alias, missing);
        Directory.Delete(missing);
        Assert.Equal(PhysicalPathResolver.Resolve(missing), PhysicalPathResolver.Resolve(m_alias));
        Assert.Throws<IOException>(() => new ArtifactPublisher(m_alias, "fixture"));
        Assert.Throws<IOException>(() => new GenerationStageCache(m_alias));
        Assert.False(Directory.Exists(missing));
        string cycle = Path.Combine(m_root, "cycle");
        await Link(cycle, m_alias);
        DeleteLink(m_alias); m_links.Remove(m_alias);
        await Link(m_alias, cycle);
        Assert.Throws<IOException>(() => PhysicalPathResolver.Resolve(cycle));
    }

    [Fact]
    public async Task AliasPublicationRecoversTheSamePhysicalJournalAndPreviousOwner()
    {
        await Link(m_alias, m_target);
        string logical = Path.Combine(m_alias, "owned"), physical = PhysicalPathResolver.Resolve(Path.Combine(m_target, "owned"));
        Publish(logical, "original");
        string parent = Path.GetDirectoryName(physical)!;
        string candidate = Path.Combine(parent, ".bgcs-staging-" + Guid.NewGuid().ToString("N"));
        string backup = Path.Combine(parent, ".bgcs-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(candidate);
        new DirectoryArtifactSink(candidate).WriteText("value.txt", "candidate");
        ImmutableArray<ArtifactDescriptor> artifacts = [Descriptor("value.txt", "candidate")];
        File.WriteAllText(Path.Combine(candidate, ".bgcs-artifacts.json"), JsonSerializer.Serialize(new { schemaVersion = 1, libraryId = "fixture", artifacts }));
        Directory.Move(physical, backup);
        Directory.Move(candidate, physical);
        string identity = OperatingSystem.IsWindows() ? physical.ToUpperInvariant() : physical;
        string journal = Path.Combine(parent, ".bgcs-publication-" + Digest(identity).value + ".json");
        File.WriteAllText(journal, JsonSerializer.Serialize(new { destination = physical, candidate, backup, libraryId = "fixture", state = "prepared", artifacts }));
        using (ArtifactPublisher recovered = new(logical, "fixture"))
            Assert.Equal("original", File.ReadAllText(Path.Combine(physical, "value.txt")));
        Assert.False(File.Exists(journal));
        Assert.False(Directory.Exists(candidate));
        Assert.False(Directory.Exists(backup));
    }

    private static void Publish(string destination, string text)
    {
        using ArtifactPublisher publisher = new(destination, "fixture");
        new DirectoryArtifactSink(publisher.candidateRoot).WriteText("value.txt", text);
        publisher.Commit([Descriptor("value.txt", text)]);
    }

    private async Task Link(string path, string target)
    {
        Assert.StartsWith(Path.GetFullPath(m_root) + Path.DirectorySeparatorChar, Path.GetFullPath(path), StringComparison.Ordinal);
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
        }
        else { Directory.CreateSymbolicLink(path, target); m_links.Add(path); }
    }

    private static ArtifactDescriptor Descriptor(string path, string text) => new(path, "text/plain", Digest(text));
    private static ContentDigest Digest(string text) => new(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))));
    private static void DeleteLink(string path) { if (OperatingSystem.IsWindows()) Directory.Delete(path); else File.Delete(path); }
    public void Dispose()
    {
        for (int index = m_links.Count - 1; index >= 0; index--) DeleteLink(m_links[index]);
        Directory.Delete(m_root, recursive: true);
    }
}
