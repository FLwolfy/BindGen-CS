using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Infrastructure.Publishing;
using Xunit;

namespace BGCS.Infrastructure.Tests;

public sealed class DirectoryArtifactReaderTests : IDisposable
{
    private readonly string m_root = Path.Combine(Path.GetTempPath(), "bgcs-candidate-read-" + Guid.NewGuid().ToString("N"));
    public DirectoryArtifactReaderTests() => Directory.CreateDirectory(m_root);

    [Fact]
    public async Task ExactDeclaredSet_IsCopiedAndItsContentIsRecheckedOnEveryRead()
    {
        byte[] content = Encoding.UTF8.GetBytes("candidate ✓");
        File.WriteAllBytes(Path.Combine(m_root, "value.txt"), content);
        ArtifactDescriptor[] artifacts = [Descriptor("value.txt", content)];
        DirectoryArtifactReader reader = new(m_root, artifacts);
        artifacts[0] = Descriptor("other.txt", []);
        Assert.Equal("candidate ✓", await reader.ReadTextAsync("value.txt", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => reader.ReadTextAsync("other.txt", CancellationToken.None).AsTask());
        File.WriteAllText(Path.Combine(m_root, "value.txt"), "changed");
        await Assert.ThrowsAsync<IOException>(() => reader.ReadBytesAsync("value.txt", CancellationToken.None).AsTask());
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("/absolute.txt")]
    [InlineData("C:/absolute.txt")]
    [InlineData("nested\\file.txt")]
    public void UnownedOrEscapingPaths_AreRejectedDuringSnapshotConstruction(string path)
        => Assert.Throws<ArgumentException>(() => new DirectoryArtifactReader(m_root, [Descriptor(path, [])]));

    [Fact]
    public async Task InvalidTextSizeLimitsAndCancellation_DoNotReturnPartialCandidates()
    {
        byte[] binary = [0xff, 0xfe];
        File.WriteAllBytes(Path.Combine(m_root, "data.bin"), binary);
        ArtifactDescriptor artifact = Descriptor("data.bin", binary);
        DirectoryArtifactReader reader = new(m_root, [artifact]);
        await Assert.ThrowsAsync<DecoderFallbackException>(() => reader.ReadTextAsync("data.bin", CancellationToken.None).AsTask());
        await Assert.ThrowsAsync<IOException>(() => new DirectoryArtifactReader(m_root, [artifact], 1).ReadBytesAsync("data.bin", CancellationToken.None).AsTask());
        using CancellationTokenSource canceled = new();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadBytesAsync("data.bin", canceled.Token).AsTask());
    }

    private static ArtifactDescriptor Descriptor(string path, byte[] bytes)
        => new(path, "text/plain", new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(bytes))));

    public void Dispose() => Directory.Delete(m_root, recursive: true);
}
