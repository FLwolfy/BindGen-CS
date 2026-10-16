using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Inputs;
using BGCS.Infrastructure.IO;
using BGCS.Infrastructure.Verification;

namespace BGCS;

// Physical snapshots remain publication-fence inputs. This separate projection
// contains only dependencies a later manifest build can actually revalidate.
internal static class NativeProofInputProjection
{
    internal static ImmutableArray<SourceSnapshot> Create(
        IEnumerable<SourceSnapshot> snapshots,
        string privateRoot,
        string candidateRoot,
        ImmutableArray<ArtifactDescriptor> artifacts,
        CancellationToken cancellationToken
    ) {
        ArgumentNullException.ThrowIfNull(snapshots);
        if (!Path.IsPathFullyQualified(privateRoot) || !Path.IsPathFullyQualified(candidateRoot) || artifacts.IsDefault)
            throw new InvalidDataException("Native proof input projection requires complete owned roots and artifact locks.");
        StringComparer paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string scratch = Path.TrimEndingDirectorySeparator(Path.GetFullPath(privateRoot));
        string physicalScratch = PhysicalPathResolver.Resolve(scratch);
        string candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidateRoot));
        string physicalCandidate = PhysicalPathResolver.Resolve(candidate);
        if (Directory.Exists(scratch) || File.Exists(scratch) || !paths.Equals(scratch, physicalScratch)
            || !Directory.Exists(candidate) || Inside(physicalScratch, physicalCandidate) || Inside(physicalCandidate, physicalScratch))
            throw new IOException("Native proof input projection requires its deleted private scratch and separate live artifact root.");

        ImmutableArray<SourceSnapshot> originals = snapshots.ToImmutableArray();
        if (SourceSnapshotBatch.GetMismatch(originals, cancellationToken) is string originalMismatch)
            throw new IOException("Native proof inputs changed before their portable projection. " + originalMismatch);
        Dictionary<string, ArtifactDescriptor> declared = new(paths);
        foreach (ArtifactDescriptor artifact in artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (artifact is null) throw new InvalidDataException("A native proof artifact lock cannot be null.");
            string relative = ArtifactDigestVerifier.ValidateRelativePath(artifact.relativePath);
            if (!declared.TryAdd(Path.GetFullPath(relative, physicalCandidate), artifact))
                throw new InvalidDataException("Native proof artifact locks contain colliding candidate identities.");
        }

        ImmutableArray<SourceSnapshot>.Builder projected = ImmutableArray.CreateBuilder<SourceSnapshot>();
        foreach (SourceSnapshot snapshot in originals)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ImmutableArray<InputDependency>.Builder files = ImmutableArray.CreateBuilder<InputDependency>();
            foreach (InputDependency input in snapshot.files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!OwnedCandidate(input.path, input.resolvedPath))
                {
                    RejectPrivate(input.path, input.resolvedPath);
                    files.Add(input);
                    continue;
                }
                if (!declared.TryGetValue(input.resolvedPath, out ArtifactDescriptor? artifact) || artifact.digest != input.digest)
                    throw new IOException("A native proof candidate input has no equal published artifact lock: " + input.path);
                // ArtifactDescriptor has no length field: independently check
                // both its current bytes and the captured input's exact length.
                using FileStream file = new(input.path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.SequentialScan);
                if (file.Length != input.length || Convert.ToHexStringLower(SHA256.HashData(file)) != artifact.digest.value)
                    throw new IOException("A native proof candidate differs from its published artifact lock: " + input.path);
            }
            ImmutableArray<DirectoryInputDependency>.Builder directories = ImmutableArray.CreateBuilder<DirectoryInputDependency>();
            foreach (DirectoryInputDependency directory in snapshot.searchDirectories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!OwnedCandidate(directory.path, directory.resolvedPath))
                {
                    RejectPrivate(directory.path, directory.resolvedPath);
                    directories.Add(directory);
                    continue;
                }
                // Only the existing whole-root output exclusion justifies
                // omitting this query. Never discard observed external trees.
                if (!snapshot.excludedDirectories.Any(exclusion => Inside(directory.resolvedPath, PhysicalPathResolver.Resolve(exclusion))))
                    throw new IOException("A native proof candidate query was not wholly excluded at capture: " + directory.path);
            }
            foreach (MissingInputDependency missing in snapshot.absentInputs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (OwnedCandidate(missing.path, missing.resolvedPath))
                    throw new NotSupportedException("A native proof candidate absence requires a portable generated-directory contract: " + missing.path);
                RejectPrivate(missing.path, missing.resolvedPath);
            }
            projected.Add(snapshot with
            {
                files = files.ToImmutable(),
                searchDirectories = directories.ToImmutable(),
                excludedDirectories = snapshot.excludedDirectories.Where(exclusion => !Inside(PhysicalPathResolver.Resolve(exclusion), physicalScratch)).ToImmutableArray()
            });
        }
        // Removing an exclusion must not silently establish a new membership
        // baseline. Recheck the original surviving directory records verbatim.
        ImmutableArray<SourceSnapshot> result = projected.ToImmutable();
        if (SourceSnapshotBatch.GetMismatch(result, cancellationToken) is string projectedMismatch)
            throw new IOException("Native proof inputs cannot retain their original portable input locks. " + projectedMismatch);
        return result;

        bool Inside(
            string path,
            string root
        ) => paths.Equals(path, root)
            || path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, comparison);

        bool OwnedCandidate(
            string logical,
            string physical
        ) {
            string absolute = Path.GetFullPath(logical);
            bool logicalOwned = Inside(absolute, candidate) || Inside(absolute, physicalCandidate);
            bool physicalOwned = Inside(physical, physicalCandidate);
            if (logicalOwned != physicalOwned)
                throw new IOException("A native proof candidate input follows an unowned alias: " + logical);
            if (!logicalOwned) return false;
            string relative = Path.GetRelativePath(Inside(absolute, candidate) ? candidate : physicalCandidate, absolute);
            if (!paths.Equals(Path.GetFullPath(relative, physicalCandidate), physical))
                throw new IOException("A native proof candidate input changed its artifact-relative physical identity: " + logical);
            return true;
        }

        void RejectPrivate(
            string logical,
            string physical
        ) {
            if (Inside(Path.GetFullPath(logical), scratch) || Inside(physical, physicalScratch))
                throw new IOException("A deleted private native proof input cannot become a manifest dependency: " + logical);
        }
    }
}
