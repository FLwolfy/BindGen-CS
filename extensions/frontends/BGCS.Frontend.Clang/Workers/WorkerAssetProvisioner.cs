using BGCS.Infrastructure.Workers;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using BGCS.Core.Artifacts;
using BGCS.Core.Common;
using BGCS.Core.Hosting;
using BGCS.Infrastructure.IO;
using BGCS.Infrastructure.Publishing;
using BGCS.Infrastructure.Verification;

namespace BGCS.Frontend.Clang.Workers;

/// <summary>Prepares a verified parser closure from application-deployed NuGet assets without build hooks or downloads.</summary>
public static class WorkerAssetProvisioner
{
    private const int C_MAXIMUM_MANIFEST_BYTES = 16 * 1024 * 1024;
    private const long C_MAXIMUM_COMMON_BYTES = 256L * 1024 * 1024;

    /// <summary>Verifies deployed resources and prepares or reuses their exact host-specific owned worker directory.</summary>
    /// <param name="applicationBase">The deployed application's base directory, containing bgcs/worker-assets and native runtime assets.</param>
    /// <param name="host">The explicit host identity and path services.</param>
    /// <param name="cacheRoot">An explicit owned resource-cache parent, or null for the user's local BGCS resource directory.</param>
    /// <param name="cancellationToken">Cancels verification and preparation before publication.</param>
    /// <returns>The complete worker directory identified by version, host and manifest digest.</returns>
    /// <exception cref="IOException">A required resource, content identity or owned deployment is invalid.</exception>
    public static string Prepare(
        string applicationBase,
        HostServices host,
        string? cacheRoot = null,
        CancellationToken cancellationToken = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationBase);
        cancellationToken.ThrowIfCancellationRequested();
        OwnedPathBoundary application = new(applicationBase, host.paths);
        string assetRoot = Path.Combine(application.physicalPath, "bgcs", "worker-assets");
        OwnedPathBoundary assets = new(assetRoot, host.paths);
        string hostRid = host.execution.hostRuntimeIdentifier;
        _ = ArtifactDigestVerifier.ValidateRelativePath(hostRid);
        string manifestPath = Path.Combine(assets.physicalPath, "manifests", hostRid + ".json");
        OwnedPathBoundary.RejectPhysicalLinks(manifestPath);
        byte[] manifestBytes;
        using (FileStream manifestStream = File.OpenRead(manifestPath))
        {
            if (manifestStream.Length > C_MAXIMUM_MANIFEST_BYTES) throw new IOException("The deployed worker manifest exceeds its size limit.");
            manifestBytes = new byte[checked((int)manifestStream.Length)];
            manifestStream.ReadExactly(manifestBytes);
        }
        WorkerAssetManifest manifest = JsonSerializer.Deserialize<WorkerAssetManifest>(manifestBytes,
            new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })
            ?? throw new IOException("The deployed worker manifest is empty.");
        WorkerAssetDeployer.ValidateManifest(manifest, hostRid);
        if (manifest.artifacts.Any(artifact => ArtifactDigestVerifier.ValidateRelativePath(artifact.relativePath) != artifact.relativePath))
            throw new IOException("Application-deployed SDK manifests must use canonical portable resource paths.");
        string identity = Convert.ToHexStringLower(SHA256.HashData(manifestBytes));
        string version = typeof(WorkerAssetProvisioner).Assembly.GetName().Version!.ToString();
        string parent = cacheRoot is null ? DefaultCacheRoot() : Path.GetFullPath(cacheRoot);
        string destination = Path.Combine(parent, version, hostRid, identity);
        using ArtifactPublisher publisher = new(destination, "bgcs-worker:" + hostRid, host.paths, [application.physicalPath], cancellationToken);
        bool reuse = File.Exists(Path.Combine(destination, "worker-assets.manifest.json"));
        string? candidate = reuse ? null : publisher.candidateRoot;
        Dictionary<string, ArtifactDescriptor> expected = manifest.artifacts.ToDictionary(artifact => artifact.relativePath, StringComparer.Ordinal);
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        string archivePath = Path.Combine(assets.physicalPath, "worker-common.zip");
        OwnedPathBoundary.RejectPhysicalLinks(archivePath);
        byte[] buffer = new byte[131072];
        using (FileStream archiveStream = File.OpenRead(archivePath))
        using (ZipArchive archive = new(archiveStream, ZipArchiveMode.Read))
        {
            if (archive.Entries.Count > expected.Count) throw new IOException("The worker archive contains unlisted entries.");
            long total = 0;
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relative = ArtifactDigestVerifier.ValidateRelativePath(entry.FullName);
                if (relative != entry.FullName || !seen.Add(relative) || !expected.TryGetValue(relative, out ArtifactDescriptor? artifact))
                    throw new IOException("The worker archive contains an unlisted, duplicate or noncanonical entry.");
                if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                    throw new IOException("The worker archive cannot contain symbolic links.");
                total = checked(total + entry.Length);
                if (entry.Length < 0 || total > C_MAXIMUM_COMMON_BYTES) throw new IOException("The worker archive exceeds its uncompressed size limit.");
                using Stream input = entry.Open();
                CopyVerified(input, artifact, candidate, buffer, entry.Length, cancellationToken);
            }
        }
        OwnedPathBoundary.RejectPhysicalLinks(archivePath);
        string nativePrefix = "runtimes/" + hostRid + "/native/";
        foreach (ArtifactDescriptor artifact in manifest.artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (seen.Contains(artifact.relativePath)) continue;
            if (!artifact.relativePath.StartsWith(nativePrefix, StringComparison.Ordinal)
                || artifact.relativePath[nativePrefix.Length..].Contains('/'))
                throw new IOException("The managed worker archive is missing a required execution dependency: " + artifact.relativePath);
            string native = Path.Combine(application.physicalPath, artifact.relativePath);
            OwnedPathBoundary.RejectPhysicalLinks(native);
            if (!File.Exists(native)) native = Path.Combine(application.physicalPath, Path.GetFileName(artifact.relativePath));
            OwnedPathBoundary.RejectPhysicalLinks(native);
            using FileStream input = File.OpenRead(native);
            CopyVerified(input, artifact, candidate, buffer, input.Length, cancellationToken);
            OwnedPathBoundary.RejectPhysicalLinks(native);
            seen.Add(artifact.relativePath);
        }
        application.Verify();
        assets.Verify();
        OwnedPathBoundary.RejectPhysicalLinks(manifestPath);
        using (FileStream current = File.OpenRead(manifestPath))
            if (Convert.ToHexStringLower(SHA256.HashData(current)) != identity)
                throw new IOException("The worker resource manifest changed during preparation.");
        WorkerAssetDeployer deployer = new(host.paths);
        if (reuse)
        {
            string preparedManifest = Path.Combine(destination, "worker-assets.manifest.json");
            OwnedPathBoundary.RejectPhysicalLinks(preparedManifest);
            using (FileStream current = File.OpenRead(preparedManifest))
                if (Convert.ToHexStringLower(SHA256.HashData(current)) != identity)
                    throw new IOException("The prepared worker manifest differs from its resource-cache identity.");
            deployer.VerifyDeployment(destination, manifest, hostRid);
            cancellationToken.ThrowIfCancellationRequested();
            return destination;
        }
        File.WriteAllBytes(Path.Combine(candidate!, "worker-assets.manifest.json"), manifestBytes);
        deployer.VerifyDeployment(candidate!, manifest, hostRid);
        ArtifactDescriptor descriptor = new("worker-assets.manifest.json", "application/json", new ContentDigest(identity));
        publisher.Commit(manifest.artifacts.Add(descriptor), cancellationToken);
        return destination;
    }

    private static string DefaultCacheRoot()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        if (!Path.IsPathFullyQualified(local)) throw new IOException("The SDK needs an absolute local resource directory; deploy an explicit worker root for this host.");
        return Path.Combine(local, "BGCS", "worker-assets");
    }

    private static void CopyVerified(
        Stream input,
        ArtifactDescriptor artifact,
        string? candidate,
        byte[] buffer,
        long expectedLength,
        CancellationToken cancellationToken
    ) {
        string? outputPath = candidate is null ? null : Path.Combine(candidate, artifact.relativePath);
        if (outputPath is not null) Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using FileStream? output = outputPath is null ? null : new(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long length = 0;
        int count;
        while ((count = input.Read(buffer)) != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            length = checked(length + count);
            if (length > expectedLength) throw new IOException("A worker resource exceeds its declared length.");
            hash.AppendData(buffer, 0, count);
            output?.Write(buffer, 0, count);
        }
        if (length != expectedLength || Convert.ToHexStringLower(hash.GetHashAndReset()) != artifact.digest.value)
            throw new IOException("Worker resource content digest mismatch: " + artifact.relativePath);
    }
}
