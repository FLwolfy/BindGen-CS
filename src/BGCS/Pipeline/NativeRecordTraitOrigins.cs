using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Requests;
using BGCS.Infrastructure.IO;
using BGCS.Infrastructure.Verification;

namespace BGCS;

internal sealed class NativeRecordTraitOrigins(
    string artifactRoot,
    ImmutableArray<ArtifactDescriptor> artifacts
)
{
    internal sealed record Capture(
        ImmutableDictionary<string, string> files,
        ImmutableDictionary<string, string> observer,
        ImmutableDictionary<string, string> directories
    );

    internal Capture Authenticate(
        NativeBuildPlan plan,
        string? proofRoot,
        ImmutableDictionary<string, string> sourceMappings,
        CancellationToken cancellationToken
    ) {
        if (!Path.IsPathFullyQualified(artifactRoot) || artifacts.IsDefault)
            throw new InvalidDataException("Native trait origins require an absolute artifact root and frozen content locks.");
        StringComparer paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string logicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(artifactRoot));
        string physicalRoot = PhysicalPathResolver.Resolve(logicalRoot);
        if (!Directory.Exists(logicalRoot) || (File.GetAttributes(logicalRoot) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Native trait origins cannot use a linked or missing artifact root.");
        Dictionary<string, string> files = new(sourceMappings, paths);
        Dictionary<string, string> observer = new(sourceMappings, paths);
        Dictionary<string, string> directories = new(paths);
        HashSet<string> identities = new(StringComparer.Ordinal);
        foreach (string identity in files.Values)
            if (!identities.Add(identity)) throw new InvalidDataException("Native trait origins repeat a logical source identity.");
        HashSet<string> declared = new(paths);
        foreach (ArtifactDescriptor artifact in artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relative = ArtifactDigestVerifier.ValidateRelativePath(artifact.relativePath);
            if (!declared.Add(relative)) throw new InvalidDataException("Native trait origins repeat an artifact identity.");
            string path = Path.GetFullPath(relative, logicalRoot);
            string physical = Path.GetFullPath(relative, physicalRoot);
            if (!paths.Equals(PhysicalPathResolver.Resolve(path), physical))
                throw new IOException("A native trait artifact changed its locked physical origin: " + relative);
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.SequentialScan);
            if (Convert.ToHexStringLower(SHA256.HashData(stream)) != artifact.digest.value)
                throw new IOException("A native trait artifact differs from its declared content digest: " + relative);
            string identity = sourceMappings.TryGetValue(path, out string? original) ? original : "native-artifacts/" + relative;
            if (!files.ContainsKey(path) && !identities.Add(identity))
                throw new InvalidDataException("A native trait artifact conflicts with an existing logical source identity: " + relative);
            Add(files, path, identity);
            Add(observer, path, identity);
        }
        AddDirectory(logicalRoot, "native-artifacts");
        if (!paths.Equals(logicalRoot, physicalRoot)) AddDirectory(physicalRoot, "native-artifacts");
        if (proofRoot is not null)
        {
            if (!Path.IsPathFullyQualified(proofRoot) || plan.scratchDirectory is not string scratch
                || !Inside(Path.GetFullPath(scratch), Path.TrimEndingDirectorySeparator(Path.GetFullPath(proofRoot))))
                throw new InvalidDataException("A native trait proof role must own the executed plan's scratch directory.");
            string proof = Path.TrimEndingDirectorySeparator(Path.GetFullPath(proofRoot));
            if (Inside(proof, logicalRoot) || Inside(logicalRoot, proof) || Inside(proof, physicalRoot) || Inside(physicalRoot, proof) || !Directory.Exists(proof)
                || !paths.Equals(PhysicalPathResolver.Resolve(proof), proof) || (File.GetAttributes(proof) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Native trait proof and artifact roles must have separate physical owners.");
            AddDirectory(proof, "native-proof");
        }
        if (!paths.Equals(PhysicalPathResolver.Resolve(logicalRoot), physicalRoot)
            || (File.GetAttributes(logicalRoot) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The native trait artifact root changed while its origins were authenticated.");
        return new(files.ToImmutableDictionary(paths), observer.ToImmutableDictionary(paths), directories.ToImmutableDictionary(paths));

        void AddDirectory(
            string root,
            string identity
        ) {
            Add(directories, root, identity + ":/");
            // Only an authenticated directory prefix can normalize descendants.
            // The observer also recognizes this prefix's exact root-valued argv.
            Add(observer, root + Path.DirectorySeparatorChar, identity + "/");
        }
        static void Add(
            Dictionary<string, string> values,
            string path,
            string identity
        ) {
            if (values.TryGetValue(path, out string? existing) && existing != identity)
                throw new InvalidDataException("Native trait origins contain conflicting logical mappings: " + path);
            values[path] = identity;
        }
        bool Inside(
            string path,
            string root
        ) => paths.Equals(path, root)
            || path.StartsWith(root + Path.DirectorySeparatorChar, comparison);
    }
}
