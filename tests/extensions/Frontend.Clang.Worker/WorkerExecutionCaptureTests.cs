using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using BGCS.Core.Artifacts;
using BGCS.Core.Common;
using BGCS.Core.Hosting;
using BGCS.Core.Inputs;
using BGCS.Core.Ports;
using BGCS.Frontend.Clang;
using BGCS.Frontend.Clang.Workers;
using BGCS.Infrastructure.IO;
using BGCS.Infrastructure.Workers;
using Xunit;

namespace BGCS.Worker.Tests;

/// <summary>Exercises the actual public worker execution identity and deployment verifier, without private reflection.</summary>
public sealed class WorkerExecutionCaptureTests : WorkerTestHost
{
    [Theory]
    [InlineData("locale\\resource.bin")]
    [InlineData("locale/RESOURCE.bin")]
    public void PortableManifestSpellingCapturesTheActualVerifiedFile(string declaredPath)
    {
        using Fixture fixture = new(host);
        string path = fixture.PathOf("locale/resource.bin"); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        byte[] bytes = Encoding.UTF8.GetBytes("independent localized resource"); File.WriteAllBytes(path, bytes);
        ContentDigest digest = Digest(bytes);
        fixture.manifest = fixture.manifest with { artifacts = fixture.manifest.artifacts.Add(new(declaredPath, "application/octet-stream", digest)) };
        fixture.WriteManifest();
        FrontendExecutionIdentity captured = fixture.Capture();
        Assert.Equal(digest, Assert.Single(captured.snapshot.files, file => host.paths.Equals(file.path, path)).digest);
        Assert.True(SourceSnapshotVerifier.Matches(captured.snapshot, host.paths));
        Assert.NotEmpty(captured.snapshot.pathSemantics);
    }
    [Theory]
    [InlineData(false, "utf8")]
    [InlineData(true, "utf8")]
    [InlineData(true, "utf16le")]
    [InlineData(true, "utf16be")]
    [InlineData(true, "utf32le")]
    [InlineData(true, "utf32be")]
    public void PublicClientPreservesDocumentEncodingAndTheCompleteExecutionClosure(bool manifestBom, string encodingName)
    {
        using Fixture fixture = new(host);
        Encoding encoding = encodingName switch { "utf8" => new UTF8Encoding(true), "utf16le" => Encoding.Unicode, "utf16be" => Encoding.BigEndianUnicode, "utf32le" => new UTF32Encoding(false, true), _ => new UTF32Encoding(true, true) };
        foreach (string relative in new[] { Fixture.Dependencies, Fixture.Configuration })
            fixture.Replace(relative, encoding.GetPreamble().Concat(encoding.GetBytes(File.ReadAllText(fixture.PathOf(relative)))).ToArray());
        fixture.WriteManifest(manifestBom);
        FrontendExecutionIdentity captured = fixture.Capture();
        Assert.True(SourceSnapshotVerifier.Matches(captured.snapshot, host.paths));
        Assert.Contains(captured.snapshot.files, file => file.path == fixture.managedHost);
        string runtime = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        Assert.Contains(captured.snapshot.searchDirectories, directory => directory.path == runtime);
        Assert.Contains(captured.snapshot.files, file => file.path == typeof(object).Assembly.Location);
        Assert.Equal(JsonSerializer.Serialize(fixture.manifest), JsonSerializer.Serialize(new WorkerAssetDeployer(host.paths).ReadManifest(fixture.PathOf("worker-assets.manifest.json"))));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonUtf8ManifestIsRejectedThroughBothPublicEntrypoints(bool utf32)
    {
        using Fixture fixture = new(host);
        File.WriteAllText(fixture.PathOf("worker-assets.manifest.json"), JsonSerializer.Serialize(fixture.manifest), utf32 ? new UTF32Encoding(false, true) : Encoding.Unicode);
        Exception? direct = Record.Exception(() => new WorkerAssetDeployer(host.paths).ReadManifest(fixture.PathOf("worker-assets.manifest.json")));
        Exception? client = Record.Exception(() => fixture.Capture());
        Assert.NotNull(direct); Assert.NotNull(client); Assert.Equal(direct.GetType(), client.GetType());
    }
    [Theory]
    [InlineData("host")]
    [InlineData("worker")]
    [InlineData("resource")]
    public void SameLengthAndTimestampChangesInvalidateCapturedBytes(string kind)
    {
        using Fixture fixture = new(host);
        FrontendExecutionIdentity captured = fixture.Capture();
        string path = kind == "host" ? fixture.managedHost : fixture.PathOf(kind == "worker" ? "BGCS.Core.dll" : "runtimes/" + fixture.rid + "/native/libclang" + fixture.extension);
        DateTime timestamp = File.GetLastWriteTimeUtc(path); byte[] bytes = File.ReadAllBytes(path); bytes[0] ^= 1;
        File.WriteAllBytes(path, bytes); File.SetLastWriteTimeUtc(path, timestamp);
        Assert.False(SourceSnapshotVerifier.Matches(captured.snapshot, host.paths));
        if (kind != "host") Assert.Throws<IOException>(() => fixture.Capture());
    }
    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("case-collision")]
    [InlineData("escape")]
    [InlineData("reserved")]
    public void PublicClientAndDeploymentVerifierRejectTheSameInvalidSets(string damage)
    {
        using Fixture fixture = new(host);
        if (damage == "missing") File.Delete(fixture.PathOf("BGCS.Core.dll"));
        else if (damage == "extra") File.WriteAllText(fixture.PathOf("unlisted.dll"), "unlisted");
        else fixture.manifest = fixture.manifest with { artifacts = fixture.manifest.artifacts.Add(damage == "case-collision"
            ? fixture.manifest.artifacts[0] with { relativePath = fixture.manifest.artifacts[0].relativePath.ToUpperInvariant() }
            : new(damage == "escape" ? "../outside.dll" : "worker-assets.manifest.json", "application/json", new(new string('0', 64)))) };
        fixture.WriteManifest();
        Assert.Throws<IOException>(() => new WorkerAssetDeployer(host.paths).VerifyDeployment(fixture.source, fixture.manifest, fixture.rid));
        Assert.Throws<IOException>(() => fixture.Capture());
    }
    [Fact]
    public void ReservedOwnershipMetadataIsNotAnExecutionInput()
    {
        using Fixture fixture = new(host); string path = fixture.PathOf(".bgcs-artifacts.json"); File.WriteAllText(path, "{}");
        Assert.DoesNotContain(fixture.Capture().snapshot.files, file => file.path == path);
    }
    [Fact]
    public void NewExecutionAssetsCannotBeAddedUnderAnExistingManifest()
    {
        using Fixture fixture = new(host); FrontendExecutionIdentity first = fixture.Capture();
        File.WriteAllText(fixture.PathOf("injected.dll"), "unlisted");
        Assert.Throws<IOException>(() => fixture.Capture());
        Assert.True(SourceSnapshotVerifier.Matches(first.snapshot, host.paths)); // The independent public verifier still rejects the new executable closure.
    }
    [Fact]
    public void CancellationRetainsItsCallerTokenBeforeWorkerExecution()
    {
        using Fixture fixture = new(host); using CancellationTokenSource cancellation = new(); cancellation.Cancel();
        OperationCanceledException failure = Assert.ThrowsAny<OperationCanceledException>(() => fixture.Capture(cancellation.Token));
        Assert.Equal(cancellation.Token, failure.CancellationToken);
    }
    private static ContentDigest Digest(byte[] bytes) => new(Convert.ToHexStringLower(SHA256.HashData(bytes)));
    private sealed class Fixture : IDisposable
    {
        internal const string Dependencies = "BGCS.Frontend.Clang.Worker.deps.json", Configuration = "BGCS.Frontend.Clang.Worker.runtimeconfig.json";
        private readonly HostServices m_host;
        private readonly string m_root = Path.Combine(Path.GetTempPath(), "bgcs-public-worker-capture-" + Guid.NewGuid().ToString("N"));
        internal string source => Path.Combine(m_root, "worker");
        internal string managedHost => Path.Combine(m_root, "dotnet-host");
        internal string rid => m_host.execution.hostRuntimeIdentifier;
        internal string extension => rid.StartsWith("win-", StringComparison.Ordinal) ? ".dll" : rid.StartsWith("osx-", StringComparison.Ordinal) ? ".dylib" : ".so";
        internal WorkerAssetManifest manifest { get; set; }
        internal Fixture(HostServices parent)
        {
            Directory.CreateDirectory(source); File.WriteAllText(managedHost, "selected managed host");
            m_host = new(parent.paths, new(parent.execution.hostRuntimeIdentifier, managedHost, parent.execution.environmentNamesCaseSensitive));
            string[] names = ["BGCS.Frontend.Clang.Worker.dll", Dependencies, Configuration, "BGCS.Core.dll", "BGCS.Infrastructure.dll", "BGCS.Frontend.Clang.dll", "BGCS.Hosting.Standard.dll", "ClangSharp.dll", "ClangSharp.Interop.dll", "runtimes/" + rid + "/native/libclang" + extension, "runtimes/" + rid + "/native/libClangSharp" + extension];
            manifest = new(1, rid, ClangParserIdentity.C_IDENTITY, names.Select(name => {
                string text = name == Dependencies ? "{\"runtimeTarget\":{\"name\":\"net10\"},\"targets\":{\"net10\":{}}}"
                    : name == Configuration ? "{\"runtimeOptions\":{\"framework\":{\"name\":\"Microsoft.NETCore.App\"},\"marker\":\"café\"}}" : "fixture:" + name;
                byte[] bytes = Encoding.UTF8.GetBytes(text); string path = PathOf(name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes);
                return new ArtifactDescriptor(name, "application/octet-stream", Digest(bytes));
            }).ToImmutableArray());
            WriteManifest();
        }
        internal string PathOf(string relative) => Path.Combine(source, relative);
        internal FrontendExecutionIdentity Capture(CancellationToken cancellationToken = default)
            => new FrontendWorkerClient(PathOf("BGCS.Frontend.Clang.Worker.dll"), m_host).CaptureExecutionIdentity(cancellationToken);
        internal void WriteManifest(bool bom = false) => File.WriteAllText(PathOf("worker-assets.manifest.json"), JsonSerializer.Serialize(manifest), new UTF8Encoding(bom));
        internal void Replace(string relative, byte[] bytes)
        {
            File.WriteAllBytes(PathOf(relative), bytes);
            manifest = manifest with { artifacts = manifest.artifacts.Select(artifact => artifact.relativePath == relative ? artifact with { digest = Digest(bytes) } : artifact).ToImmutableArray() };
            WriteManifest();
        }
        public void Dispose() => Directory.Delete(m_root, recursive: true);
    }
}
