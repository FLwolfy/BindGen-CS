using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;

namespace BGCS.Infrastructure.Verification;

/// <summary>Checks portable native source requirements against an exact published artifact set.</summary>
public static class NativeSourceManifestVerifier
{
    /// <summary>Rejects incomplete, escaping, conflicting or stale source requirements without running native tools.</summary>
    /// <param name="manifest">The detached source manifest.</param>
    /// <param name="artifacts">The complete verified publication.</param>
    public static void Validate(
        NativeSourceManifest manifest,
        ImmutableArray<ArtifactDescriptor> artifacts
    ) {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.schemaVersion != 1 || string.IsNullOrWhiteSpace(manifest.targetId) || string.IsNullOrWhiteSpace(manifest.triple)
            || manifest.language is not (SourceLanguage.C or SourceLanguage.Cpp) || string.IsNullOrWhiteSpace(manifest.languageStandard)
            || manifest.sources.IsDefault || manifest.headers.IsDefault || manifest.inputRoots.IsDefault || manifest.inputs.IsDefault
            || manifest.includeDirectories.IsDefault || manifest.headerReferences.IsDefault || manifest.defines.IsDefault
            || manifest.requiredExports.IsDefault || manifest.languageRequirements.IsDefault || artifacts.IsDefault)
            throw new InvalidDataException("The native source manifest has an unsupported revision or incomplete requirements.");
        Path(manifest.targetId);
        Dictionary<string, NativeInputRoot> roots = new(StringComparer.Ordinal);
        foreach (NativeInputRoot root in manifest.inputRoots)
        {
            Path(root.id);
            if (root.id.Contains('/') || root.kind is not ("user" or "project" or "sdk" or "sdk-root" or "generated") || !roots.TryAdd(root.id, root))
                throw new InvalidDataException("The native source manifest has an invalid or duplicate root identity.");
        }
        if (roots.Values.Count(root => root.kind == "generated") != 1 || !roots.TryGetValue("generated", out var generated) || generated.kind != "generated")
            throw new InvalidDataException("The native source manifest must identify its generated publication root.");
        Dictionary<string, ArtifactDescriptor> published = artifacts.ToDictionary(artifact => artifact.relativePath, StringComparer.Ordinal);
        HashSet<string> files = new(StringComparer.Ordinal);
        foreach (NativeInputFile input in manifest.inputs)
        {
            RequireRoot(input.rootId); Path(input.relativePath);
            _ = new ContentDigest(input.digest.value);
            if (input.length < 0 || input.role is not ("EntryHeader" or "Dependency" or "ShimSource") || !files.Add(input.rootId + "/" + input.relativePath))
                throw new InvalidDataException("The native source manifest has invalid or conflicting input files.");
            if (input.rootId == "generated" && (!published.TryGetValue(input.relativePath, out var artifact) || artifact.digest != input.digest))
                throw new InvalidDataException("A generated native input does not match its published content identity.");
        }
        foreach (NativeIncludeDirectory include in manifest.includeDirectories)
        {
            RequireRoot(include.rootId);
            if (include.relativePath.Length != 0) Path(include.relativePath);
        }
        foreach (NativeHeaderReference header in manifest.headerReferences)
        {
            RequireRoot(header.rootId); Path(header.relativePath); Path(header.include);
            if (!files.Contains(header.rootId + "/" + header.relativePath))
                throw new InvalidDataException("A native header reference has no captured input identity.");
        }
        HashSet<string> emitted = new(StringComparer.OrdinalIgnoreCase);
        if (manifest.sources.Any(artifact => artifact.mediaType is not ("text/x-c" or "text/x-c++"))
            || manifest.headers.Any(artifact => artifact.mediaType is not ("text/x-c-header" or "text/x-c++-header")))
            throw new InvalidDataException("Native source and header requirements must retain their declared artifact roles.");
        if (manifest.sources.Any(artifact => System.IO.Path.GetExtension(artifact.relativePath) is not (".c" or ".cpp" or ".cc" or ".cxx"))
            || manifest.headers.Any(artifact => System.IO.Path.GetExtension(artifact.relativePath).ToLowerInvariant() is not (".h" or ".hpp" or ".hh" or ".hxx")))
            throw new InvalidDataException("Native source and header artifact suffixes must agree with their declared roles.");
        foreach (ArtifactDescriptor artifact in manifest.sources.Concat(manifest.headers))
        {
            Path(artifact.relativePath);
            if (!artifact.relativePath.StartsWith(manifest.targetId + "/native/", StringComparison.Ordinal)
                || !emitted.Add(artifact.relativePath) || !published.TryGetValue(artifact.relativePath, out var actual) || artifact != actual)
                throw new InvalidDataException("A native source requirement does not match the verified publication.");
        }
        foreach (ArtifactDescriptor artifact in artifacts.Where(artifact => artifact.relativePath.StartsWith(manifest.targetId + "/native/", StringComparison.Ordinal)
            && artifact.mediaType is "text/x-c" or "text/x-c++" or "text/x-c-header" or "text/x-c++-header"))
            if (!emitted.Contains(artifact.relativePath)) throw new InvalidDataException("The native source manifest omitted a generated source or header.");
        if (manifest.requiredExports.Any(string.IsNullOrWhiteSpace) || manifest.defines.Any(value => value.Contains('\0') || value.Contains('\n') || value.Contains('\r')))
            throw new InvalidDataException("A native source requirement contains an invalid export or definition.");

        void RequireRoot(string id)
        {
            if (!roots.ContainsKey(id)) throw new InvalidDataException("A native source requirement refers to an unknown root.");
        }
        static void Path(string path)
        {
            if (ArtifactDigestVerifier.ValidateRelativePath(path) != path || path.Any(character => character < 32))
                throw new InvalidDataException("Native source identities must be canonical portable relative paths.");
        }
    }
}
