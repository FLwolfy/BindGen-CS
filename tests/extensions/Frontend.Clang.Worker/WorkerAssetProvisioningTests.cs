using BGCS.Frontend.Clang;
using BGCS.Frontend.Clang.Workers;
using BGCS.Frontend.Clang.Parsing.Arguments;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Artifacts;
using BGCS.Core.Common;
using BGCS.Infrastructure.Publishing;
using BGCS.Infrastructure.Workers;
using Xunit;

namespace BGCS.Worker.Tests;

public sealed class WorkerAssetProvisioningTests : WorkerTestHost
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApplicationAssetsPrepareExactHostClosureAndReuseItWithoutRewriting(bool flattenedNative)
    {
        using Scratch scratch = new(flattenedNative);
        string root = WorkerAssetProvisioner.Prepare(scratch.application, host, scratch.cache);
        WorkerAssetDeployer deployer = new(host.paths);
        deployer.VerifyDeployment(root, scratch.manifest, scratch.hostRid);
        Assert.Equal(scratch.manifest.artifacts.Length + 2, Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length);
        string entry = Path.Combine(root, "BGCS.Frontend.Clang.Worker.dll");
        DateTime original = File.GetLastWriteTimeUtc(entry);
        Assert.Equal(root, WorkerAssetProvisioner.Prepare(scratch.application, host, scratch.cache));
        Assert.Equal(original, File.GetLastWriteTimeUtc(entry));
        Assert.StartsWith(Path.GetFullPath(scratch.cache) + Path.DirectorySeparatorChar, root);
    }

    [Theory]
    [InlineData("../escaped.dll")]
    [InlineData("unlisted.dll")]
    [InlineData("/escaped.dll")]
    [InlineData("include/../../escaped.dll")]
    [InlineData("include/evil:stream")]
    [InlineData("BGCS.Core.dll/child")]
    [InlineData("BGCS.Core.dll")]
    [InlineData("bgcs.core.dll")]
    public void ArchiveRejectsEscapingUnlistedAndDuplicateEntriesBeforePublication(string entry)
    {
        using Scratch scratch = new();
        scratch.EditArchive(archive =>
        {
            using StreamWriter writer = new(archive.CreateEntry(entry).Open());
            writer.Write("unexpected");
        });
        Assert.Throws<IOException>(() => WorkerAssetProvisioner.Prepare(scratch.application, host, scratch.cache));
        Assert.Empty(Directory.GetFiles(scratch.cache, "BGCS.Frontend.Clang.Worker.dll", SearchOption.AllDirectories));
        Assert.False(File.Exists(Path.Combine(scratch.root, "escaped.dll")));
    }

    [Fact]
    public void ArchiveRejectsSymbolicLinkEntries()
    {
        using Scratch scratch = new();
        scratch.EditArchive(archive => archive.GetEntry("BGCS.Core.dll")!.ExternalAttributes = unchecked((int)0xA1FF0000));
        Assert.Throws<IOException>(() => WorkerAssetProvisioner.Prepare(scratch.application, host, scratch.cache));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AlteredApplicationResourcesCannotBeHiddenByAnExistingPreparedWorker(bool native)
    {
        using Scratch scratch = new();
        string root = WorkerAssetProvisioner.Prepare(scratch.application, host, scratch.cache);
        string entry = Path.Combine(root, "BGCS.Frontend.Clang.Worker.dll");
        byte[] original = File.ReadAllBytes(entry);
        if (native) File.WriteAllText(scratch.nativeFiles[0], "altered native bytes");
        else scratch.EditArchive(archive =>
        {
            archive.GetEntry("BGCS.Core.dll")!.Delete();
            using StreamWriter writer = new(archive.CreateEntry("BGCS.Core.dll").Open());
            writer.Write("altered managed bytes");
        });
        Assert.Throws<IOException>(() => WorkerAssetProvisioner.Prepare(scratch.application, host, scratch.cache));
        Assert.Equal(original, File.ReadAllBytes(entry));
        new WorkerAssetDeployer(host.paths).VerifyDeployment(root, scratch.manifest, scratch.hostRid);
    }

    [Fact]
    public void MissingManagedDependencyIsNotResolvedFromTheHostOrGlobalCaches()
    {
        using Scratch scratch = new();
        scratch.EditArchive(archive => archive.GetEntry("ClangSharp.Interop.dll")!.Delete());
        Assert.Throws<IOException>(() => WorkerAssetProvisioner.Prepare(scratch.application, host, scratch.cache));
    }

    [Fact]
    public void WrongHostManifestAndMissingHostAssetsAreRejected()
    {
        using Scratch scratch = new();
        File.WriteAllText(scratch.manifestPath, JsonSerializer.Serialize(scratch.manifest with { hostRid = "another-host" }));
        Assert.Throws<IOException>(() => WorkerAssetProvisioner.Prepare(scratch.application, host, scratch.cache));
        File.Delete(scratch.manifestPath);
        Assert.Throws<FileNotFoundException>(() => WorkerAssetProvisioner.Prepare(scratch.application, host, scratch.cache));
    }

    [Fact]
    public void CorruptedPreparedWorkerIsDiagnosedInsteadOfOverwritingUnknownBytes()
    {
        using Scratch scratch = new();
        string root = WorkerAssetProvisioner.Prepare(scratch.application, host, scratch.cache);
        string entry = Path.Combine(root, "BGCS.Frontend.Clang.Worker.dll");
        File.WriteAllText(entry, "corrupted prepared entry");
        Assert.Throws<IOException>(() => WorkerAssetProvisioner.Prepare(scratch.application, host, scratch.cache));
        Assert.Equal("corrupted prepared entry", File.ReadAllText(entry));
    }

    [Fact]
    public void PreparedManifestCannotChangeUnderAnExistingResourceCacheIdentity()
    {
        using Scratch scratch = new();
        string root = WorkerAssetProvisioner.Prepare(scratch.application, host, scratch.cache);
        string manifest = Path.Combine(root, "worker-assets.manifest.json");
        File.AppendAllText(manifest, " ");
        Assert.Throws<IOException>(() => WorkerAssetProvisioner.Prepare(scratch.application, host, scratch.cache));
    }

    [Fact]
    public void ExplicitlyClearedOwnedWorkerCanBePreparedAgain()
    {
        using Scratch scratch = new();
        string root = WorkerAssetProvisioner.Prepare(scratch.application, host, scratch.cache);
        Assert.True(ArtifactPublisher.ClearOwned(root, "bgcs-worker:" + scratch.hostRid, host.paths, [scratch.application]));
        Assert.False(File.Exists(Path.Combine(root, "worker-assets.manifest.json")));
        Assert.Equal(root, WorkerAssetProvisioner.Prepare(scratch.application, host, scratch.cache));
        new WorkerAssetDeployer(host.paths).VerifyDeployment(root, scratch.manifest, scratch.hostRid);
    }

    [Fact]
    public async Task ConcurrentPreparationPublishesOneCompleteOwnedClosure()
    {
        using Scratch scratch = new();
        string[] roots = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() => WorkerAssetProvisioner.Prepare(scratch.application, host, scratch.cache))));
        Assert.Equal(roots[0], roots[1]);
        new WorkerAssetDeployer(host.paths).VerifyDeployment(roots[0], scratch.manifest, scratch.hostRid);
    }

    [Fact]
    public void CancellationAndOverlappingApplicationCacheCannotPublish()
    {
        using Scratch scratch = new();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => WorkerAssetProvisioner.Prepare(scratch.application, host, scratch.cache, cancellation.Token));
        Assert.Throws<IOException>(() => WorkerAssetProvisioner.Prepare(scratch.application, host, Path.Combine(scratch.application, "cache")));
        Assert.Empty(Directory.GetFiles(scratch.cache, "*", SearchOption.AllDirectories));
    }

    private sealed class Scratch : IDisposable
    {
        internal string root { get; } = Path.Combine(Path.GetTempPath(), "bgcs packaged worker " + Guid.NewGuid().ToString("N"));
        internal string hostRid => RuntimeInformation.RuntimeIdentifier;
        internal string application => Path.Combine(root, "application");
        internal string cache => Path.Combine(root, "cache");
        internal string manifestPath => Path.Combine(application, "bgcs", "worker-assets", "manifests", hostRid + ".json");
        private string archivePath => Path.Combine(application, "bgcs", "worker-assets", "worker-common.zip");
        internal WorkerAssetManifest manifest { get; }
        internal List<string> nativeFiles { get; } = [];

        internal Scratch(bool flattenedNative = false)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
            Directory.CreateDirectory(cache);
            string extension = hostRid.StartsWith("win-", StringComparison.Ordinal) ? ".dll" : hostRid.StartsWith("osx-", StringComparison.Ordinal) ? ".dylib" : ".so";
            string[] common = ["BGCS.Frontend.Clang.Worker.dll", "BGCS.Frontend.Clang.Worker.deps.json", "BGCS.Frontend.Clang.Worker.runtimeconfig.json", "BGCS.Core.dll", "BGCS.Infrastructure.dll", "BGCS.Frontend.Clang.dll", "BGCS.Hosting.Standard.dll", "ClangSharp.dll", "ClangSharp.Interop.dll"];
            List<ArtifactDescriptor> artifacts = [];
            using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                foreach (string name in common)
                {
                    byte[] content = Encoding.UTF8.GetBytes(name.EndsWith(".deps.json", StringComparison.Ordinal)
                        ? "{\"runtimeTarget\":{\"name\":\"net10\"},\"targets\":{\"net10\":{}}}" : "fixture:" + name);
                    using Stream output = archive.CreateEntry(name).Open();
                    output.Write(content);
                    artifacts.Add(Describe(name, content));
                }
            }
            foreach (string library in new[] { "libclang", "libClangSharp" })
            {
                string relative = "runtimes/" + hostRid + "/native/" + library + extension;
                string path = Path.Combine(application, flattenedNative ? Path.GetFileName(relative) : relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                byte[] content = Encoding.UTF8.GetBytes("native fixture:" + relative);
                File.WriteAllBytes(path, content);
                nativeFiles.Add(path);
                artifacts.Add(Describe(relative, content));
            }
            manifest = new(1, hostRid, ClangParserIdentity.C_IDENTITY, [.. artifacts]);
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));
        }

        internal void EditArchive(Action<ZipArchive> edit)
        {
            using ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Update);
            edit(archive);
        }

        private static ArtifactDescriptor Describe(
            string path,
            byte[] bytes
        )
            => new(path, "application/octet-stream", new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(bytes))));

        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
