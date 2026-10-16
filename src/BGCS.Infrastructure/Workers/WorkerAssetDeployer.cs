using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Infrastructure.Publishing;
using BGCS.Infrastructure.Verification;

namespace BGCS.Infrastructure.Workers;

/// <summary>Copies a hash-verified host worker closure without runtime NuGet or global LLVM discovery.</summary>
public sealed class WorkerAssetDeployer
{
    /// <summary>Verifies an already-deployed worker's required execution closure and exact file bytes without launching it.</summary>
    /// <param name="root">The explicit deployed worker directory.</param>
    /// <param name="manifest">The complete immutable managed/native asset manifest.</param>
    /// <param name="hostRid">The generator host RID that must match the native parser resources.</param>
    public void VerifyDeployment(
        string root,
        WorkerAssetManifest manifest,
        string hostRid
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(manifest);
        string source = Path.GetFullPath(root);
        ValidateManifest(manifest, hostRid);
        ValidateManagedDependencies(source, manifest);
        new ArtifactDigestVerifier().Verify(source, manifest.artifacts,
            File.Exists(Path.Combine(source, ".bgcs-artifacts.json")) ? ["worker-assets.manifest.json", ".bgcs-artifacts.json"] : ["worker-assets.manifest.json"]);
    }

    /// <summary>Deploys the exact manifest collection as one owned output set.</summary>
    /// <param name="sourceRoot">The explicit restored or packaged asset root.</param>
    /// <param name="destinationRoot">The application-deployed worker root.</param>
    /// <param name="manifest">The expected host closure and exact content digests.</param>
    /// <param name="hostRid">The resolved generator host RID.</param>
    /// <param name="cancellationToken">Cancels copy or publication before commit.</param>
    public void Deploy(
        string sourceRoot,
        string destinationRoot,
        WorkerAssetManifest manifest,
        string hostRid,
        CancellationToken cancellationToken = default
    ) {
        ArgumentNullException.ThrowIfNull(manifest);
        string source = Path.GetFullPath(sourceRoot);
        ValidateManifest(manifest, hostRid);
        ValidateManagedDependencies(source, manifest);
        new ArtifactDigestVerifier().Verify(source, manifest.artifacts, Directory.EnumerateFiles(source, "*.xml").Concat(Directory.EnumerateFiles(source, "*.pdb"))
            .Concat(Directory.EnumerateFiles(source, "*.exe")).Select(path => Path.GetFileName(path))
            .Concat(new[] { "worker-assets.manifest.json", "BGCS.Frontend.Clang.Worker" }.Where(name => File.Exists(Path.Combine(source, name)))));
        using ArtifactPublisher publisher = new(destinationRoot, "bgcs-worker:" + hostRid, [source], cancellationToken);
        foreach (ArtifactDescriptor artifact in manifest.artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relative = ArtifactDigestVerifier.ValidateRelativePath(artifact.relativePath);
            string path = Path.Combine(publisher.candidateRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(Path.Combine(source, relative), path, overwrite: false);
        }
        string content = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + "\n";
        string name = "worker-assets.manifest.json";
        new DirectoryArtifactSink(publisher.candidateRoot).WriteText(name, content);
        ArtifactDescriptor descriptor = new(name, "application/json", new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content)))));
        publisher.Commit(manifest.artifacts.Add(descriptor), cancellationToken);
    }

    /// <summary>Reads a package-provided worker manifest with strict revision and field handling.</summary>
    /// <param name="path">The manifest input path.</param>
    /// <returns>The complete immutable worker closure.</returns>
    public WorkerAssetManifest ReadManifest(string path)
    {
        using FileStream stream = File.OpenRead(path);
        if (stream.Length > 16 * 1024 * 1024) throw new IOException("The worker asset manifest exceeds its size limit.");
        return JsonSerializer.Deserialize<WorkerAssetManifest>(stream, new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })
            ?? throw new IOException("The worker asset manifest is empty.");
    }

    private static void ValidateManifest(
        WorkerAssetManifest manifest,
        string hostRid
    ) {
        if (manifest.schemaVersion != 1 || manifest.hostRid != hostRid || manifest.parserIdentity != WorkerProtocol.C_PARSER_IDENTITY || manifest.artifacts.IsDefaultOrEmpty)
            throw new IOException("The worker asset manifest does not match this generator host and parser identity.");
        string[] required = ["BGCS.Frontend.Clang.Worker.dll", "BGCS.Frontend.Clang.Worker.deps.json", "BGCS.Frontend.Clang.Worker.runtimeconfig.json", "BGCS.Contracts.dll", "BGCS.Infrastructure.dll", "BGCS.Frontend.Clang.dll", "ClangSharp.dll", "ClangSharp.Interop.dll"];
        foreach (string name in required)
            if (!manifest.artifacts.Any(artifact => artifact.relativePath == name)) throw new IOException($"Worker execution dependency '{name}' is missing from the manifest.");
        string prefix = "runtimes/" + hostRid + "/native/";
        string extension = hostRid.StartsWith("win-", StringComparison.Ordinal) ? ".dll" : hostRid.StartsWith("osx-", StringComparison.Ordinal) ? ".dylib" : ".so";
        foreach (string name in new[] { "libclang", "libClangSharp" })
            if (!manifest.artifacts.Any(artifact => artifact.relativePath == prefix + name + extension))
                throw new IOException($"The worker asset manifest is missing native parser '{name}' for this host.");
        if (manifest.artifacts.Any(artifact => artifact.relativePath.StartsWith("runtimes/", StringComparison.Ordinal)
            && Path.GetFileName(artifact.relativePath).StartsWith("libclang", StringComparison.OrdinalIgnoreCase)
            && !artifact.relativePath.StartsWith(prefix, StringComparison.Ordinal)))
            throw new IOException("The worker closure includes an unrelated native parser RID.");
    }

    private static void ValidateManagedDependencies(
        string source,
        WorkerAssetManifest manifest
    ) {
        using JsonDocument dependencies = JsonDocument.Parse(File.ReadAllText(Path.Combine(source, "BGCS.Frontend.Clang.Worker.deps.json")));
        string target = dependencies.RootElement.GetProperty("runtimeTarget").GetProperty("name").GetString() ?? throw new IOException("The worker dependency target is missing.");
        WorkerDependencyClosure.Verify(dependencies.RootElement.GetProperty("targets").GetProperty(target), manifest);
    }
}
