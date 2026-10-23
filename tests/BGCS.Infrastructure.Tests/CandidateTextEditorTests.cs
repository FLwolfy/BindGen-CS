using BGCS.Core.Hosting;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Artifacts;
using BGCS.Core.Common;
using BGCS.Infrastructure.Publishing;
using Xunit;

namespace BGCS.Infrastructure.Tests;

public sealed class CandidateTextEditorTests : IDisposable
{
    private readonly IHostPathService m_paths = HostTestPaths.Create();
    private readonly string m_root = Path.Combine(Path.GetTempPath(), "bgcs-candidate-edit-" + Guid.NewGuid().ToString("N"));
    public CandidateTextEditorTests() => Directory.CreateDirectory(m_root);

    [Fact]
    public async Task Replacement_UpdatesItsDigestAndClosesRetainedEditorAccess()
    {
        byte[] original = Encoding.UTF8.GetBytes("original");
        File.WriteAllBytes(Path.Combine(m_root, "source.cs"), original);
        using CandidateTextEditor editor = new(m_root, [new("source.cs", "text/x-csharp", Digest(original))], m_paths);
        ArtifactDescriptor changed = await editor.ReplaceTextAsync("source.cs", "// revised ✓", CancellationToken.None);
        Assert.Equal(Digest(Encoding.UTF8.GetBytes("// revised ✓")), changed.digest);
        Assert.Equal(changed, Assert.Single(editor.artifacts));
        Assert.Equal("// revised ✓", await editor.ReadTextAsync("source.cs", CancellationToken.None));
        Assert.Empty(Directory.GetFiles(m_root, ".bgcs-edit-*"));
        editor.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => editor.ReplaceTextAsync("source.cs", "late", CancellationToken.None).AsTask());
        Assert.Throws<ObjectDisposedException>(() => editor.artifacts);
    }

    [Fact]
    public async Task AdditionalHelpers_StayInsideTheirOwnedPrefixAndCannotClaimExistingPaths()
    {
        using CandidateTextEditor editor = new(m_root, [], m_paths, "managed/");
        ArtifactDescriptor helper = await editor.AddTextAsync("managed/Helper.cs", "text/x-csharp", "public class Helper {}", CancellationToken.None);
        Assert.Equal(helper, Assert.Single(editor.artifacts));
        Assert.Equal("public class Helper {}", await editor.ReadTextAsync(helper.relativePath, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => editor.AddTextAsync("managed/helper.cs", "text/x-csharp", "changed", CancellationToken.None).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => editor.AddTextAsync("native/helper.h", "text/plain", "bad", CancellationToken.None).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => editor.AddTextAsync("managed/../escape.cs", "text/plain", "bad", CancellationToken.None).AsTask());
        using CandidateTextEditor replacementsOnly = new(m_root, [helper], m_paths);
        await Assert.ThrowsAsync<ArgumentException>(() => replacementsOnly.AddTextAsync("managed/Other.cs", "text/x-csharp", "bad", CancellationToken.None).AsTask());
        await editor.DeleteTextAsync(helper.relativePath, CancellationToken.None);
        Assert.Empty(editor.artifacts);
        Assert.False(File.Exists(Path.Combine(m_root, helper.relativePath)));
        await Assert.ThrowsAsync<ArgumentException>(() => editor.DeleteTextAsync(helper.relativePath, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task UnownedPathsExternalMutationAndCancellation_CannotOverwriteCandidateText()
    {
        byte[] original = Encoding.UTF8.GetBytes("original");
        string path = Path.Combine(m_root, "source.cs");
        File.WriteAllBytes(path, original);
        using CandidateTextEditor editor = new(m_root, [new("source.cs", "text/x-csharp", Digest(original))], m_paths);
        await Assert.ThrowsAsync<ArgumentException>(() => editor.ReplaceTextAsync("../escape.cs", "bad", CancellationToken.None).AsTask());
        using CancellationTokenSource canceled = new();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => editor.ReplaceTextAsync("source.cs", "bad", canceled.Token).AsTask());
        Assert.Equal("original", File.ReadAllText(path));
        File.WriteAllText(path, "external change");
        await Assert.ThrowsAsync<IOException>(() => editor.ReplaceTextAsync("source.cs", "bad", CancellationToken.None).AsTask());
        Assert.Equal("external change", File.ReadAllText(path));
    }

    private static ContentDigest Digest(byte[] bytes) => new(Convert.ToHexStringLower(SHA256.HashData(bytes)));
    public void Dispose() => Directory.Delete(m_root, recursive: true);
}
