using System;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using BGCS.Core.Execution;
using BGCS.Core.IO;
using Xunit;

namespace BGCS.Core.Tests;

public sealed class OutputDirectoryTransactionTests : IDisposable
{
    private readonly string m_root = Path.Combine(Path.GetTempPath(), "BgcsOutputOwnership", Guid.NewGuid().ToString("N"));

    public OutputDirectoryTransactionTests() => Directory.CreateDirectory(m_root);

    [Fact]
    public void TimeoutDoesNotCreateAnotherCandidateOrReplaceCurrentOutput()
    {
        string output = Path.Combine(m_root, "output");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "value.txt"), "current");
        using var owner = new OutputDirectoryTransaction(output);
        Assert.Throws<TimeoutException>(() => new OutputDirectoryTransaction(output, TimeSpan.FromMilliseconds(60)));
        Assert.Equal("current", File.ReadAllText(Path.Combine(output, "value.txt")));
        Assert.Single(Directory.GetDirectories(m_root, ".bgcs-staging-*"));
    }

    [Fact]
    public void NestedPublicationDoesNotRepeatLongDestinationNamesAndCommitsBothCandidates()
    {
        string name = new('a', 120);
        string output = Path.Combine(m_root, name);
        using var outer = new OutputDirectoryTransaction(output);
        Assert.DoesNotContain(name, Path.GetFileName(outer.stagingPath));
        using (var inner = new OutputDirectoryTransaction(Path.Combine(outer.stagingPath, "Native")))
        {
            File.WriteAllText(Path.Combine(inner.stagingPath, "api.h"), "complete");
            inner.Commit();
        }
        outer.Commit();
        Assert.Equal("complete", File.ReadAllText(Path.Combine(output, "Native", "api.h")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TemporaryReadersReleaseBeforePublicationCompletes(bool blockCandidate)
    {
        if (!OperatingSystem.IsWindows())
            return;
        string output = Path.Combine(m_root, "output");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "value.txt"), "previous");
        using var publication = new OutputDirectoryTransaction(output);
        File.WriteAllText(Path.Combine(publication.stagingPath, "value.txt"), "candidate");
        string blocked = Path.Combine(blockCandidate ? publication.stagingPath : output, "value.txt");
        using var reader = new FileStream(blocked, FileMode.Open, FileAccess.Read, FileShare.Read);
        Task commit = Task.Run(publication.Commit);
        await Task.Delay(80);
        Assert.False(commit.IsCompleted);
        reader.Dispose();
        await commit.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("candidate", File.ReadAllText(Path.Combine(output, "value.txt")));
        Assert.Empty(Directory.GetDirectories(m_root, ".bgcs-backup-*"));
    }

    [Fact]
    public void PersistentCandidateReadersPreserveBothTreesAndPermitRetryAfterRelease()
    {
        if (!OperatingSystem.IsWindows())
            return;
        string output = Path.Combine(m_root, "output");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "value.txt"), "previous");
        using var publication = new OutputDirectoryTransaction(output);
        string candidate = Path.Combine(publication.stagingPath, "value.txt");
        File.WriteAllText(candidate, "candidate");
        using var reader = new FileStream(candidate, FileMode.Open, FileAccess.Read, FileShare.Read);
        Stopwatch elapsed = Stopwatch.StartNew();
        Exception? failure = Record.Exception(publication.Commit);
        Assert.True(failure is IOException or UnauthorizedAccessException);
        Assert.InRange(elapsed.Elapsed, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5));
        Assert.Equal("previous", File.ReadAllText(Path.Combine(output, "value.txt")));
        Assert.Equal("candidate", File.ReadAllText(candidate));
        reader.Dispose();
        publication.Commit();
        Assert.Equal("candidate", File.ReadAllText(Path.Combine(output, "value.txt")));
    }

    [Fact]
    public void DisposedTransactionsReleaseOwnershipAndRejectPublication()
    {
        string output = Path.Combine(m_root, "output");
        var first = new OutputDirectoryTransaction(output);
        first.Dispose();
        first.Dispose();
        Assert.Throws<ObjectDisposedException>(first.Commit);
        using var second = new OutputDirectoryTransaction(output, TimeSpan.Zero);
        File.WriteAllText(Path.Combine(second.stagingPath, "value.txt"), "next");
        second.Commit();
        Assert.Equal("next", File.ReadAllText(Path.Combine(output, "value.txt")));
    }

    [Fact]
    public void BackupCleanupFailureReportsThatTheCandidateIsAlreadyInstalled()
    {
        if (!OperatingSystem.IsWindows())
            return;
        string output = Path.Combine(m_root, "output");
        Directory.CreateDirectory(output);
        string previous = Path.Combine(output, "value.txt");
        File.WriteAllText(previous, "previous");
        File.SetAttributes(previous, FileAttributes.ReadOnly);
        try
        {
            using var candidate = new OutputDirectoryTransaction(output);
            File.WriteAllText(Path.Combine(candidate.stagingPath, "value.txt"), "candidate");

            IOException failure = Assert.Throws<IOException>(candidate.Commit);

            Assert.Contains("was installed, but backup cleanup failed", failure.Message);
            Assert.Equal("candidate", File.ReadAllText(Path.Combine(output, "value.txt")));
            string backup = Assert.Single(Directory.GetDirectories(m_root, ".bgcs-backup-*"));
            Assert.Contains(backup, failure.Message);
            Assert.Equal("previous", File.ReadAllText(Path.Combine(backup, "value.txt")));
            Assert.Throws<InvalidOperationException>(candidate.Commit);
        }
        finally
        {
            foreach (string file in Directory.EnumerateFiles(m_root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task IndependentProcessesSerializePublicationAndCancellationReleasesTheWaiter()
    {
        string output = Path.Combine(m_root, "output");
        string acquired = Path.Combine(m_root, "acquired");
        string release = Path.Combine(m_root, "release");
        string repository = FindRepository();
        string configuration = typeof(OutputDirectoryTransactionTests).Assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        string fixture = Path.Combine(repository, "tests", "fixtures", "OutputPublisher", "bin", configuration, "net9.0", "OutputPublisher.dll");
        string dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        using var childCancellation = new CancellationTokenSource();
        Task<ProcessExecutionResult> child = ProcessExecutor.ExecuteAsync(
            dotnet, [fixture, output, acquired, release], repository, TimeSpan.FromSeconds(30), childCancellation.Token);
        try
        {
            using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!File.Exists(acquired))
                await Task.Delay(20, startup.Token);
            using var waiting = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.Run(() =>
            {
                using var rejected = new OutputDirectoryTransaction(output, cancellationToken: waiting.Token);
            }));
            Assert.False(Directory.Exists(output));
            File.WriteAllText(release, "release");
            ProcessExecutionResult completed = await child;
            Assert.False(completed.timedOut);
            Assert.Equal(0, completed.exitCode);
            Assert.Equal("child", File.ReadAllText(Path.Combine(output, "value.txt")));
            using var next = new OutputDirectoryTransaction(output, TimeSpan.Zero);
            File.WriteAllText(Path.Combine(next.stagingPath, "value.txt"), "parent");
            next.Commit();
            Assert.Equal("parent", File.ReadAllText(Path.Combine(output, "value.txt")));
        }
        finally
        {
            childCancellation.Cancel();
            try
            {
                await child;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    public void Dispose() => Directory.Delete(m_root, recursive: true);

    private static string FindRepository()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BindGen-CS.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("The BGCS fixture checkout was not found.");
    }
}
