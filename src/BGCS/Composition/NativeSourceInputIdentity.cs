using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Infrastructure.Verification;
using BGCS.Infrastructure.Workers;

namespace BGCS.Composition;

// This is a cache-key projection, never a replacement for the physical
// snapshots used before publication. Candidate search roots are already
// explicitly excluded from membership, while every file actually read is
// still content-hashed and the native preprocessor is rerun on each request.
internal static class NativeSourceInputIdentity
{
    internal static JsonObject Evidence(
        SourceSnapshot snapshot,
        IReadOnlyDictionary<string, string> mappings,
        IEnumerable<string>? excludedDirectories = null
    ) {
        JsonObject evidence = JsonSerializer.SerializeToNode(Create(snapshot, mappings), WorkerProtocol.jsonOptions)!.AsObject();
        evidence["excludedDirectories"] = JsonSerializer.SerializeToNode((excludedDirectories ?? snapshot.excludedDirectories).ToArray(), WorkerProtocol.jsonOptions);
        return evidence;
    }

    internal static object Create(
        SourceSnapshot snapshot,
        IReadOnlyDictionary<string, string> mappings
    ) {
        StringComparer paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        Dictionary<string, string> files = new(paths);
        Dictionary<string, string?> directories = new(paths);
        HashSet<string> identities = new(StringComparer.Ordinal);
        foreach (var mapping in mappings)
        {
            if (!Path.IsPathFullyQualified(mapping.Key)) throw new InvalidDataException("An owned source mapping must identify an absolute physical input.");
            string physical = Path.GetFullPath(mapping.Key);
            string logical = ArtifactDigestVerifier.ValidateRelativePath(mapping.Value);
            if (!files.TryAdd(physical, "owned-source:/" + logical) || !identities.Add(logical)) throw new InvalidDataException("Owned source mappings repeat a physical or logical input.");
            string parent = Path.GetDirectoryName(physical)!;
            string logicalParent = (Path.GetDirectoryName(logical) ?? string.Empty).Replace('\\', '/');
            if (!directories.TryAdd(parent, "owned-source:/" + logicalParent)
                && directories[parent] != "owned-source:/" + logicalParent) directories[parent] = null;
        }
        return new
        {
            files = snapshot.files.Select(file => files.TryGetValue(file.path, out string? logical)
                ? file with { path = logical, resolvedPath = paths.Equals(file.path, file.resolvedPath) ? logical : file.resolvedPath }
                : file).OrderBy(file => file.path, StringComparer.Ordinal).ToArray(),
            searchDirectories = snapshot.searchDirectories.Select(directory =>
            {
                if (directories.TryGetValue(directory.path, out string? logical) && logical is not null
                    && paths.Equals(directory.path, directory.resolvedPath) && snapshot.excludedDirectories.Any(root => Inside(directory.path, root)))
                    return new SearchIdentity(logical, logical, directory.exists, null, true);
                return new SearchIdentity(directory.path, directory.resolvedPath, directory.exists, directory.digest, false);
            }).ToArray(),
            snapshot.absentInputs
        };

        bool Inside(
            string path,
            string root
        ) {
            string absolute = Path.GetFullPath(path), parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            return paths.Equals(absolute, parent) || absolute.StartsWith(parent + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
    }

    private sealed record SearchIdentity(
        string path,
        string resolvedPath,
        bool exists,
        ContentDigest? digest,
        bool ownedMembershipExcluded
    );
}
