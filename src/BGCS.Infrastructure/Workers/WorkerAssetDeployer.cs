using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Infrastructure.IO;
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

    internal (WorkerAssetManifest Manifest, SourceSnapshot Snapshot, string RuntimeConfiguration) CaptureVerifiedDeployment(
        string root,
        string hostRid,
        string runtimeConfigurationPath,
        IEnumerable<DeclaredExternalInput> extraExecutionInputs,
        CancellationToken cancellationToken
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(extraExecutionInputs);
        string source = Path.GetFullPath(root);
        OwnedPathBoundary boundary = new(source);
        // Reject linked deployment members before reading even the small JSON
        // inputs. This traversal is shared with ordinary artifact verification.
        HashSet<string> initialFiles = ReadFileSet();
        byte[] manifestBytes = Read("worker-assets.manifest.json", 16 * 1024 * 1024);
        WorkerAssetManifest manifest;
        using (MemoryStream stream = new(manifestBytes, writable: false))
            manifest = JsonSerializer.Deserialize<WorkerAssetManifest>(stream,
                new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })
                ?? throw new IOException("The worker asset manifest is empty.");
        ValidateManifest(manifest, hostRid);
        Dictionary<string, ArtifactDescriptor> expected = new(StringComparer.OrdinalIgnoreCase);
        foreach (ArtifactDescriptor artifact in manifest.artifacts)
        {
            string relative = ArtifactDigestVerifier.ValidateRelativePath(artifact.relativePath);
            if (!expected.TryAdd(relative, artifact)) throw new IOException($"Duplicate artifact path '{relative}'.");
        }
        HashSet<string> additional = new(StringComparer.OrdinalIgnoreCase) { "worker-assets.manifest.json" };
        if (File.Exists(Path.Combine(source, ".bgcs-artifacts.json"))) additional.Add(".bgcs-artifacts.json");
        if (additional.Overlaps(expected.Keys)) throw new IOException("An artifact path is also reserved as publication metadata.");
        VerifyFileSet(initialFiles);
        const string dependenciesPath = "BGCS.Frontend.Clang.Worker.deps.json";
        string runtimeRelative = ArtifactDigestVerifier.ValidateRelativePath(Path.GetRelativePath(source, runtimeConfigurationPath));
        byte[] dependencyBytes = Read(dependenciesPath);
        byte[] runtimeBytes = Read(runtimeRelative);
        ValidateManagedDependencies(source, manifest, Text(dependencyBytes));

        ImmutableArray<DeclaredExternalInput> inputs = expected.Keys
            .Select(relative => new DeclaredExternalInput(Path.Combine(source, ActualSpelling(relative)), ExternalInputKind.File))
            .Append(new(Path.Combine(source, "worker-assets.manifest.json"), ExternalInputKind.File))
            .Concat(extraExecutionInputs).ToImmutableArray();
        // Capture includes its existing independent post-capture Matches pass.
        // No digest is memoized across that fence or any later verification.
        SourceSnapshot snapshot = new ExternalInputSnapshotBuilder().Capture(inputs, source, cancellationToken);
        StringComparer pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        Dictionary<string, InputDependency> observed = snapshot.files.ToDictionary(input => input.path, pathComparer);
        foreach (var entry in expected)
            if (Observation(entry.Key).digest != entry.Value.digest)
                throw new IOException($"Artifact content digest mismatch: '{entry.Key}'.");
        VerifyRead("worker-assets.manifest.json", manifestBytes);
        VerifyRead(dependenciesPath, dependencyBytes);
        VerifyRead(runtimeRelative, runtimeBytes);
        boundary.Verify();
        VerifyFileSet(ReadFileSet());
        boundary.Verify();
        return (manifest, snapshot, Text(runtimeBytes));

        HashSet<string> ReadFileSet()
        {
            boundary.Verify();
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            foreach (string file in ArtifactDigestVerifier.EnumerateFiles(boundary.physicalPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relative = Path.GetRelativePath(boundary.physicalPath, file).Replace('\\', '/');
                if (!seen.Add(relative)) throw new IOException($"Artifact case collision at '{relative}'.");
            }
            boundary.Verify();
            return seen;
        }
        void VerifyFileSet(HashSet<string> seen)
        {
            foreach (string relative in seen)
                if (!expected.ContainsKey(relative) && !additional.Contains(relative))
                    throw new IOException($"Unlisted artifact '{relative}' cannot be published.");
            foreach (string relative in expected.Keys.Concat(additional))
                if (!seen.Contains(relative)) throw new IOException($"Required artifact '{relative}' is missing.");
        }
        byte[] Read(
            string relative,
            int maximumBytes = int.MaxValue
        ) {
            cancellationToken.ThrowIfCancellationRequested();
            boundary.Verify();
            string path = Path.Combine(boundary.physicalPath, relative);
            OwnedPathBoundary.RejectPhysicalLinks(path);
            using FileStream stream = File.OpenRead(path);
            if (stream.Length > maximumBytes) throw new IOException("The worker asset input exceeds its size limit.");
            byte[] bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            boundary.Verify();
            return bytes;
        }
        InputDependency Observation(string relative)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string actual = ActualSpelling(relative);
            string logical = Path.GetFullPath(Path.Combine(source, actual));
            if (!observed.TryGetValue(logical, out InputDependency? input)
                || !pathComparer.Equals(input.resolvedPath, Path.GetFullPath(Path.Combine(boundary.physicalPath, actual))))
                throw new IOException($"A worker asset changed its physical identity during capture: '{relative}'.");
            return input;
        }
        string ActualSpelling(string relative)
        {
            if (!initialFiles.TryGetValue(relative, out string? actual))
                throw new IOException($"Required artifact '{relative}' is missing.");
            return actual;
        }
        void VerifyRead(
            string relative,
            byte[] bytes
        ) {
            InputDependency input = Observation(relative);
            if (input.length != bytes.LongLength || input.digest.value != Convert.ToHexStringLower(SHA256.HashData(bytes)))
                throw new IOException($"A worker asset changed between reading and snapshot capture: '{relative}'.");
        }
        static string Text(byte[] bytes)
        {
            using MemoryStream stream = new(bytes, writable: false);
            using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
    }

    internal static void ValidateManifest(
        WorkerAssetManifest manifest,
        string hostRid
    ) {
        if (manifest.schemaVersion != 1 || manifest.hostRid != hostRid || manifest.parserIdentity != WorkerProtocol.C_PARSER_IDENTITY || manifest.artifacts.IsDefaultOrEmpty)
            throw new IOException("The worker asset manifest does not match this generator host and parser identity.");
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        foreach (ArtifactDescriptor artifact in manifest.artifacts)
        {
            string path = ArtifactDigestVerifier.ValidateRelativePath(artifact.relativePath);
            if (!paths.Add(path)
                || path.Equals("worker-assets.manifest.json", StringComparison.OrdinalIgnoreCase)
                || path.Equals(".bgcs-artifacts.json", StringComparison.OrdinalIgnoreCase))
                throw new IOException("The worker asset manifest contains duplicate or reserved paths.");
            _ = new ContentDigest(artifact.digest.value);
        }
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
        WorkerAssetManifest manifest,
        string? capturedText = null
    ) {
        using JsonDocument dependencies = JsonDocument.Parse(capturedText ?? File.ReadAllText(Path.Combine(source, "BGCS.Frontend.Clang.Worker.deps.json")));
        string target = dependencies.RootElement.GetProperty("runtimeTarget").GetProperty("name").GetString() ?? throw new IOException("The worker dependency target is missing.");
        WorkerDependencyClosure.Verify(dependencies.RootElement.GetProperty("targets").GetProperty(target), manifest);
    }
}
