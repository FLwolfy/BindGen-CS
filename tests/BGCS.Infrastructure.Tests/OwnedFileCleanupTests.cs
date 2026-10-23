using System.Linq;
using BGCS.Core.Hosting;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Artifacts;
using BGCS.Core.Common;
using BGCS.Infrastructure.Execution;
using BGCS.Infrastructure.IO;
using BGCS.Infrastructure.Publishing;
using Xunit;

namespace BGCS.Infrastructure.Tests;

public sealed class OwnedFileCleanupTests : IDisposable
{
    private readonly IHostPathService m_paths = HostTestPaths.Create();
    private readonly string m_root;
    private string? m_link;

    public OwnedFileCleanupTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-owned-cleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        m_root = PhysicalPathResolver.Resolve(root, m_paths);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ReplacedPhysicalParentCannotRedirectPendingFileCleanup(
        bool nested,
        bool priorFailure
    ) {
        string owner = Path.Combine(m_root, "owner"), foreign = Path.Combine(m_root, "foreign"), moved = Path.Combine(m_root, "moved");
        string relative = nested ? Path.Combine("nested", "original.txt") : "original.txt";
        string original = Path.Combine(owner, relative), foreignFile = Path.Combine(foreign, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(original)!);
        Directory.CreateDirectory(Path.GetDirectoryName(foreignFile)!);
        File.WriteAllText(original, "original", new UTF8Encoding(false));
        File.WriteAllText(foreignFile, "unrelated foreign bytes");
        string replacement = Path.Combine(m_root, "replacement");
        await Link(replacement, foreign);
        FaultInjectingPaths observed = new(m_paths);
        ArtifactDescriptor descriptor = new(relative.Replace('\\', '/'), "text/plain", new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("original")))));
        using CandidateTextEditor editor = new(owner, [descriptor], observed);
        observed.beforeEquality = (
            _,
            _
        ) => {
            if (!Directory.EnumerateFiles(owner, ".bgcs-edit-*", SearchOption.AllDirectories).Any()) return;
            observed.beforeEquality = null;
            Directory.Move(owner, moved);
            Directory.Move(replacement, owner);
            m_link = owner;
            if (priorFailure) throw new InvalidDataException("Original post-write verification failure");
        };
        AggregateException failure = await Assert.ThrowsAsync<AggregateException>(async () => await editor.ReplaceTextAsync(relative.Replace('\\', '/'), "replacement", default));
        Assert.Equal(2, failure.InnerExceptions.Count);
        if (priorFailure) Assert.IsType<InvalidDataException>(failure.InnerExceptions[0]);
        else Assert.IsType<IOException>(failure.InnerExceptions[0]);
        Assert.IsType<IOException>(failure.InnerExceptions[1]);
        Assert.Equal("unrelated foreign bytes", File.ReadAllText(foreignFile));
        Assert.Equal("original", File.ReadAllText(Path.Combine(moved, relative)));
        Assert.Single(Directory.EnumerateFiles(moved, ".bgcs-edit-*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task UnchangedOwnerRemovesOnlyItsPendingFileAndAcceptsAlreadyAbsentFiles()
    {
        string original = Path.Combine(m_root, "original.txt"), retained = Path.Combine(m_root, "retained.txt");
        File.WriteAllText(original, "original", new UTF8Encoding(false));
        File.WriteAllText(retained, "retained");
        ArtifactDescriptor descriptor = new("original.txt", "text/plain", new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("original")))));
        using CandidateTextEditor editor = new(m_root, [descriptor], m_paths);
        await editor.ReplaceTextAsync("original.txt", "replacement", default);
        Assert.Empty(Directory.EnumerateFiles(m_root, ".bgcs-edit-*", SearchOption.AllDirectories));
        Assert.Equal("replacement", File.ReadAllText(original));
        Assert.Equal("retained", File.ReadAllText(retained));
        await editor.ReplaceTextAsync("original.txt", "next", default);
        Assert.Empty(Directory.EnumerateFiles(m_root, ".bgcs-edit-*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedEditorOperationPreservesItsErrorAndReleasesOwnership(bool addition)
    {
        string original = Path.Combine(m_root, "original.txt");
        File.WriteAllText(original, "original", new UTF8Encoding(false));
        ArtifactDescriptor descriptor = new("original.txt", "text/plain", new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("original")))));
        CandidateTextEditor editor = new(m_root, [descriptor], m_paths, "extra/");
        await Assert.ThrowsAsync<EncoderFallbackException>(async () =>
        {
            if (addition) await editor.AddTextAsync("extra/new.txt", "text/plain", "\ud800", default);
            else await editor.ReplaceTextAsync("original.txt", "\ud800", default);
        });
        Assert.Equal("original", File.ReadAllText(original));
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        if (addition)
        {
            await editor.AddTextAsync("extra/new.txt", "text/plain", "valid", timeout.Token);
            Assert.Equal("valid", await editor.ReadTextAsync("extra/new.txt", timeout.Token));
        }
        else
        {
            await editor.ReplaceTextAsync("original.txt", "valid", timeout.Token);
            Assert.Equal("valid", await editor.ReadTextAsync("original.txt", timeout.Token));
        }
        editor.Dispose();
    }

    [Fact]
    public void JournalCreateNewCollisionPreservesThePreExistingPendingJournal()
    {
        string output = Path.Combine(m_root, "output");
        FaultInjectingPaths observed = new(m_paths);
        using ArtifactPublisher publisher = new(output, "fixture", observed);
        new DirectoryArtifactSink(publisher.candidateRoot, observed).WriteText("value.txt", "value");
        byte[] pending = Encoding.UTF8.GetBytes("previous recovery journal bytes");
        string journal = Path.Combine(publisher.candidateRoot, ".bgcs-artifacts.json");
        observed.beforeEquality = (
            left,
            _
        ) => {
            if (!m_paths.Equals(left, journal)) return;
            observed.beforeEquality = null;
            File.WriteAllBytes(journal + ".tmp", pending);
        };
        ArtifactDescriptor descriptor = new("value.txt", "text/plain", new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("value")))));
        Assert.Throws<IOException>(() => publisher.Commit([descriptor]));
        Assert.Equal(pending, File.ReadAllBytes(journal + ".tmp"));
        Assert.False(File.Exists(journal));
    }

    private async Task Link(
        string path,
        string target
    ) {
        Assert.StartsWith(m_root + Path.DirectorySeparatorChar, Path.GetFullPath(path), StringComparison.Ordinal);
        if (OperatingSystem.IsWindows())
        {
            string executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            string command = "$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path '" + path.Replace("'", "''", StringComparison.Ordinal)
                + "' -Target '" + target.Replace("'", "''", StringComparison.Ordinal) + "' | Out-Null";
            var result = await ProcessExecutor.ExecuteAsync(executable,
                ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(command))],
                m_root, TimeSpan.FromSeconds(30));
            if (new DirectoryInfo(path).LinkTarget is not null) m_link = path;
            Assert.True(result.exitCode == 0, result.standardOutput + Environment.NewLine + result.standardError);
        }
        else { Directory.CreateSymbolicLink(path, target); m_link = path; }
    }

    public void Dispose()
    {
        if (m_link is not null)
        {
            if (OperatingSystem.IsWindows()) Directory.Delete(m_link);
            else File.Delete(m_link);
        }
        Directory.Delete(m_root, recursive: true);
    }
}
