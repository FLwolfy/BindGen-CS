using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Infrastructure.Execution;
using BGCS.Infrastructure.IO;
using BGCS.Infrastructure.Publishing;
using Xunit;

namespace BGCS.Infrastructure.Tests;

public sealed class OwnedFileCleanupTests : IDisposable
{
    private static readonly Action<string?, Exception?> s_delete = typeof(CandidateTextEditor).Assembly
        .GetType("BGCS.Infrastructure.IO.OwnedFileCleanup", throwOnError: true)!
        .GetMethod("Delete", BindingFlags.Static | BindingFlags.NonPublic)!
        .CreateDelegate<Action<string?, Exception?>>();
    private readonly string m_root;
    private string? m_link;

    public OwnedFileCleanupTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-owned-cleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        m_root = PhysicalPathResolver.Resolve(root);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ReplacedPhysicalParentCannotRedirectPendingFileCleanup(bool nested, bool priorFailure)
    {
        string owner = Path.Combine(m_root, "owner"), foreign = Path.Combine(m_root, "foreign"), moved = Path.Combine(m_root, "moved");
        string relative = nested ? Path.Combine("nested", "pending.tmp") : "pending.tmp";
        string pending = Path.Combine(owner, relative), foreignFile = Path.Combine(foreign, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(pending)!);
        Directory.CreateDirectory(Path.GetDirectoryName(foreignFile)!);
        File.WriteAllText(pending, "owned pending bytes");
        File.WriteAllText(foreignFile, "unrelated foreign bytes");

        // The file is already pending when its captured physical owner is replaced.
        // Invoke the production cleanup boundary directly to avoid a timing-dependent
        // race between FileStream disposal and an asynchronous continuation.
        Directory.Move(owner, moved);
        await Link(owner, foreign);
        InvalidDataException original = new("The post-write ownership verification failed.");
        if (priorFailure)
        {
            AggregateException failure = Assert.Throws<AggregateException>(() => s_delete(pending, original));
            Assert.Collection(failure.InnerExceptions,
                error => Assert.Same(original, error),
                error => Assert.IsType<IOException>(error));
        }
        else Assert.Throws<IOException>(() => s_delete(pending, null));

        Assert.Equal("unrelated foreign bytes", File.ReadAllText(foreignFile));
        Assert.Equal("owned pending bytes", File.ReadAllText(Path.Combine(moved, relative)));
    }

    [Fact]
    public void UnchangedOwnerRemovesOnlyItsPendingFileAndAcceptsAlreadyAbsentFiles()
    {
        string pending = Path.Combine(m_root, "pending.tmp"), retained = Path.Combine(m_root, "retained.txt");
        File.WriteAllText(pending, "pending");
        File.WriteAllText(retained, "retained");
        s_delete(pending, new InvalidOperationException("Original operation failure"));
        Assert.False(File.Exists(pending));
        Assert.Equal("retained", File.ReadAllText(retained));
        s_delete(pending, null);
        s_delete(null, null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedEditorOperationPreservesItsErrorAndReleasesOwnership(bool addition)
    {
        string original = Path.Combine(m_root, "original.txt");
        File.WriteAllText(original, "original", new UTF8Encoding(false));
        ArtifactDescriptor descriptor = new("original.txt", "text/plain", new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("original")))));
        CandidateTextEditor editor = new(m_root, [descriptor], "extra/");
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
        Action<string, Dictionary<string, string>> write = typeof(CandidateTextEditor).Assembly
            .GetType("BGCS.Infrastructure.Publishing.PublicationJournalStorage", throwOnError: true)!
            .GetMethod("Write", BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(typeof(Dictionary<string, string>))
            .CreateDelegate<Action<string, Dictionary<string, string>>>();
        string journal = Path.Combine(m_root, "publication.json");
        byte[] pending = Encoding.UTF8.GetBytes("previous recovery journal bytes");
        File.WriteAllBytes(journal + ".tmp", pending);

        Assert.Throws<IOException>(() => write(journal, new() { ["state"] = "new" }));

        Assert.Equal(pending, File.ReadAllBytes(journal + ".tmp"));
        Assert.False(File.Exists(journal));
    }

    private async Task Link(string path, string target)
    {
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
