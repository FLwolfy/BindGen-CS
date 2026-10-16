using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using BGCS.Contracts.Inputs;
using BGCS.Infrastructure.IO;
using Xunit;

namespace BGCS.Infrastructure.Tests;

public sealed class ParallelInputSnapshotTests : IDisposable
{
    private readonly string m_root = Path.Combine(Path.GetTempPath(), "bgcs-parallel-input-snapshot-" + Guid.NewGuid().ToString("N"));

    public ParallelInputSnapshotTests() => Directory.CreateDirectory(m_root);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothLargeFileAndManyFileClosuresVerifyAllBytesDespiteEqualLengthAndTimestamp(bool manyFiles)
    {
        SourceSnapshot snapshot = Capture(manyFiles);
        InputDependency dependency = snapshot.files[^1];
        byte[] original = File.ReadAllBytes(dependency.path);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(original)), dependency.digest.value);
        Assert.True(SourceSnapshotVerifier.Matches(snapshot));
        DateTime timestamp = File.GetLastWriteTimeUtc(dependency.path);
        byte[] changed = (byte[])original.Clone();
        changed[^1] ^= 0xff;
        File.WriteAllBytes(dependency.path, changed);
        File.SetLastWriteTimeUtc(dependency.path, timestamp);
        Assert.Equal("Input file bytes changed: " + dependency.path, SourceSnapshotVerifier.GetMismatch(snapshot));
        File.WriteAllBytes(dependency.path, original);
        File.SetLastWriteTimeUtc(dependency.path, timestamp);
        Assert.True(SourceSnapshotVerifier.Matches(snapshot));
    }

    [Fact]
    public void EarliestDependencyWinsEvenWhenItsHashFinishesAfterALaterMissingFile()
    {
        SourceSnapshot snapshot = Capture(manyFiles: true);
        string large = Path.Combine(m_root, "z-original-first.bin");
        byte[] bytes = Bytes(8388645);
        File.WriteAllBytes(large, bytes);
        SourceSnapshot first = new ExternalInputSnapshotBuilder().Capture([new(large, ExternalInputKind.File)], m_root);
        snapshot = snapshot with { files = first.files.AddRange(snapshot.files) };
        InputDependency earliest = snapshot.files[0], later = snapshot.files[1];
        DateTime timestamp = File.GetLastWriteTimeUtc(earliest.path);
        bytes[^1] ^= 0xff;
        File.WriteAllBytes(earliest.path, bytes);
        File.SetLastWriteTimeUtc(earliest.path, timestamp);
        File.Delete(later.path);
        for (int repeat = 0; repeat < 8; repeat++)
            Assert.Equal("Input file bytes changed: " + earliest.path, SourceSnapshotVerifier.GetMismatch(snapshot));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancelledVerificationPreservesTheCallerTokenForBothActivationRoutes(bool manyFiles)
    {
        SourceSnapshot snapshot = Capture(manyFiles);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        OperationCanceledException failure = Assert.ThrowsAny<OperationCanceledException>(() => SourceSnapshotVerifier.GetMismatch(snapshot, cancellation.Token));
        Assert.Equal(cancellation.Token, failure.CancellationToken);
    }

    [Fact]
    public void AbsencePrecedesParallelFileMismatchesWhichPrecedeDirectoryMembership()
    {
        SourceSnapshot snapshot = Capture(manyFiles: true);
        string absent = Path.Combine(m_root, "missing.input");
        string directory = Path.Combine(m_root, "inputs");
        snapshot = snapshot with
        {
            absentInputs = [new(absent, PhysicalPathResolver.Resolve(absent))],
            searchDirectories = [DirectoryMembershipDigest.Capture(directory)]
        };
        InputDependency dependency = snapshot.files[0];
        byte[] original = File.ReadAllBytes(dependency.path), changed = (byte[])original.Clone();
        changed[^1] ^= 0xff;
        File.WriteAllBytes(dependency.path, changed);
        File.WriteAllText(absent, "now exists");
        File.WriteAllText(Path.Combine(directory, "new.header"), "new include candidate");
        Assert.Equal("An absent input was created or redirected: " + absent, SourceSnapshotVerifier.GetMismatch(snapshot));
        File.Delete(absent);
        Assert.Equal("Input file bytes changed: " + dependency.path, SourceSnapshotVerifier.GetMismatch(snapshot));
        File.WriteAllBytes(dependency.path, original);
        Assert.Equal("Include or library search membership changed: " + directory, SourceSnapshotVerifier.GetMismatch(snapshot));
    }

    private SourceSnapshot Capture(bool manyFiles)
    {
        string directory = Path.Combine(m_root, "inputs");
        Directory.CreateDirectory(directory);
        ImmutableArray<DeclaredExternalInput>.Builder inputs = ImmutableArray.CreateBuilder<DeclaredExternalInput>();
        for (int index = 0; index < (manyFiles ? 64 : 1); index++)
        {
            string path = Path.Combine(directory, index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture) + ".bin");
            File.WriteAllBytes(path, Bytes(manyFiles ? 257 : 8388645));
            inputs.Add(new(path, ExternalInputKind.File));
        }
        return new ExternalInputSnapshotBuilder().Capture(inputs.ToImmutable(), m_root);
    }

    private static byte[] Bytes(int length) => Enumerable.Range(0, length).Select(index => (byte)(index * 31)).ToArray();

    public void Dispose() => Directory.Delete(m_root, recursive: true);
}
