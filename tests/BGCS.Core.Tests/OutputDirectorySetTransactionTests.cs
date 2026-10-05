using System;
using System.IO;
using BGCS.Core.IO;
using Xunit;

namespace BGCS.Core.Tests;

public sealed class OutputDirectorySetTransactionTests : IDisposable
{
    private readonly string m_root = Path.Combine(Path.GetTempPath(), "bgcs-output-set-" + Guid.NewGuid().ToString("N"));

    public OutputDirectorySetTransactionTests() => Directory.CreateDirectory(m_root);

    [Fact]
    public void CommitInstallsEveryCandidateAsOneValidatedSet()
    {
        string native = Path.Combine(m_root, "Native");
        string managed = Path.Combine(m_root, "Generated");
        WritePrevious(native);
        WritePrevious(managed);
        using var transaction = new OutputDirectorySetTransaction([native, managed]);
        File.WriteAllText(Path.Combine(transaction.GetStagingPath(native), "api.h"), "new native");
        File.WriteAllText(Path.Combine(transaction.GetStagingPath(managed), "Bindings.cs"), "new managed");

        transaction.Commit();

        Assert.Equal("new native", File.ReadAllText(Path.Combine(native, "api.h")));
        Assert.Equal("new managed", File.ReadAllText(Path.Combine(managed, "Bindings.cs")));
        Assert.False(File.Exists(Path.Combine(native, "previous.txt")));
        Assert.Empty(Directory.GetDirectories(m_root, ".bgcs-backup-*"));
        Assert.Throws<InvalidOperationException>(transaction.Commit);
    }

    [Fact]
    public void LaterInstallationFailureRestoresEarlierDirectory()
    {
        string first = Path.Combine(m_root, "A");
        string second = Path.Combine(m_root, "B");
        WritePrevious(first);
        File.WriteAllText(second, "destination is a file");
        using (var transaction = new OutputDirectorySetTransaction([second, first]))
        {
            File.WriteAllText(Path.Combine(transaction.GetStagingPath(first), "candidate.txt"), "candidate");
            File.WriteAllText(Path.Combine(transaction.GetStagingPath(second), "candidate.txt"), "candidate");

            Assert.Throws<IOException>(transaction.Commit);

            Assert.Equal("previous", File.ReadAllText(Path.Combine(first, "previous.txt")));
            Assert.False(File.Exists(Path.Combine(first, "candidate.txt")));
            Assert.Equal("destination is a file", File.ReadAllText(second));
            Assert.Throws<InvalidOperationException>(transaction.Commit);
        }
        Assert.Empty(Directory.GetDirectories(m_root, ".bgcs-staging-*"));
        Assert.Empty(Directory.GetDirectories(m_root, ".bgcs-backup-*"));
    }

    [Fact]
    public void AcquisitionFailureReleasesAlreadyAcquiredOwners()
    {
        string first = Path.Combine(m_root, "A");
        string second = Path.Combine(m_root, "B");
        using var held = new OutputDirectoryTransaction(second);

        Assert.Throws<TimeoutException>(() => new OutputDirectorySetTransaction([second, first], TimeSpan.FromMilliseconds(80)));

        using var released = new OutputDirectoryTransaction(first, TimeSpan.Zero);
        Assert.Equal(2, Directory.GetDirectories(m_root, ".bgcs-staging-*").Length);
    }

    [Fact]
    public void RepeatedAndNestedOutputsAreRejectedBeforeOwnershipAcquisition()
    {
        string root = Path.Combine(m_root, "Output");

        Assert.Throws<ArgumentException>(() => new OutputDirectorySetTransaction([root, root]));
        Assert.Throws<ArgumentException>(() => new OutputDirectorySetTransaction([root, Path.Combine(root, "Generated")]));

        Assert.Empty(Directory.GetFiles(m_root));
        Assert.Empty(Directory.GetDirectories(m_root));
    }

    public void Dispose() => Directory.Delete(m_root, true);

    private static void WritePrevious(string path)
    {
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "previous.txt"), "previous");
    }
}
