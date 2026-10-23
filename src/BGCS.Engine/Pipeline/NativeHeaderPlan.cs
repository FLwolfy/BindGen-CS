using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using BGCS.Core.Artifacts;
using BGCS.Core.Hosting;
using BGCS.Core.Bridge;
using BGCS.Core.Inputs;
using BGCS.Core.Requests;
using BGCS.Infrastructure.IO;
using BGCS.Infrastructure.Verification;

namespace BGCS;

/// <summary>Separates physical parse inputs, portable input identities and emitted include spellings.</summary>
internal sealed class NativeHeaderPlan
{
    private sealed record Root(
        string id,
        string kind,
        string path,
        string physical,
        bool system,
        bool framework
    );
    private readonly ImmutableArray<Root> m_roots;
    private readonly ImmutableArray<NativeGeneratedInputMap> m_generated;
    private readonly FrontendRequest m_request;
    private readonly string m_candidate;
    private readonly ImmutableHashSet<string> m_executionFiles;
    private readonly IHostPathService m_paths;

    private NativeHeaderPlan(
        FrontendRequest request,
        ImmutableArray<NativeGeneratedInputMap> generated,
        string candidate,
        SourceSnapshot? executionInputs,
        IHostPathService paths
    ) {
        m_paths = paths;
        m_request = request;
        m_generated = generated;
        m_candidate = candidate;
        m_executionFiles = (executionInputs?.files ?? []).Select(file => file.resolvedPath).ToImmutableHashSet(m_paths.comparer);
        List<Root> roots = [];
        foreach (string directory in request.includeDirectories) Add(directory, "user", false, false);
        if (request.language == SourceLanguage.Cpp)
            foreach (string directory in request.target.toolchain.cxxSystemIncludeFolders) Add(directory, "sdk", true, false);
        int builtin = -1;
        for (int index = 0; index < request.headerSearchPaths.Length; index++)
            if (request.headerSearchPaths[index].isBuiltin) { builtin = index; break; }
        foreach (var directory in request.headerSearchPaths.Take(builtin < 0 ? 0 : builtin))
            if (!directory.isBuiltin) Add(directory.path, "sdk", true, directory.isFramework);
        foreach (string directory in request.target.toolchain.systemIncludeFolders) Add(directory, "sdk", true, false);
        foreach (var directory in request.headerSearchPaths.Skip(builtin + 1))
            if (!directory.isBuiltin) Add(directory.path, "sdk", true, directory.isFramework);
        foreach (string directory in request.systemIncludeDirectories) Add(directory, "sdk", true, false);
        // A sysroot identifies SDK dependencies outside its individual include directories.
        // It is not implicitly added as an include directory.
        if (request.target.toolchain.sysRoot is { } sysroot) Add(sysroot, "sdk-root", true, false);
        m_roots = roots.ToImmutableArray();

        void Add(
            string directory,
            string kind,
            bool system,
            bool framework
        ) {
            string full = Path.GetFullPath(directory, request.pathBase);
            if (roots.Any(root => m_paths.comparer.Equals(root.path, full))) return;
            string? artifact = NativeGeneratedInputRelocation.MapPath(full, generated, m_paths);
            if (artifact is not null)
            {
                roots.Add(new("generated", "generated", full, PhysicalPathResolver.Resolve(full, m_paths), system, framework));
                return;
            }
            if (kind == "user" && request.pathBase.Length != 0 && m_paths.comparer.Equals(full, Path.GetFullPath(request.pathBase))) kind = "project";
            roots.Add(new(kind == "project" ? "project" : kind + "-" + roots.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), kind,
                full, PhysicalPathResolver.Resolve(full, m_paths), system, framework));
        }
    }

    internal static NativeHeaderPlan Create(
        FrontendRequest request,
        ImmutableArray<NativeGeneratedInputMap> generated,
        string candidate,
        SourceSnapshot? executionInputs,
        IHostPathService paths
    )
        => new(request, generated, candidate, executionInputs, paths);

    /// <summary>Adds the default project root only when entry headers need it, before parsing and snapshotting.</summary>
    internal static ImmutableArray<string> PrepareUserIncludes(
        IEnumerable<string> includes,
        IEnumerable<string> headers,
        string pathBase,
        IHostPathService paths
    ) {
        ImmutableArray<string> roots = includes.Select(path => Path.GetFullPath(path, pathBase)).Distinct(paths.comparer).ToImmutableArray();
        if (headers.All(header => CanResolve(Path.GetFullPath(header, pathBase), roots, paths))) return roots;
        string project = Path.GetFullPath(pathBase);
        roots = roots.Add(project).Distinct(paths.comparer).ToImmutableArray();
        foreach (string header in headers)
            if (!CanResolve(Path.GetFullPath(header, pathBase), roots, paths))
                throw new IOException("A portable native include cannot identify '" + header + "'. Declare an unambiguous source.includeDirectories root for this input.");
        return roots;
    }

    internal string Include(
        string path,
        string sourceArtifactDirectory
    ) {
        string full = Path.GetFullPath(path, m_request.pathBase);
        string? artifact = NativeGeneratedInputRelocation.MapPath(full, m_generated, m_paths);
        if (artifact is not null)
            return IncludeArtifact(artifact["artifact:/".Length..], sourceArtifactDirectory);
        string physical = PhysicalPathResolver.Resolve(full, m_paths);
        foreach (string candidate in Candidates(full, m_roots.Where(root => root.kind != "generated").Select(root => root.path), m_paths))
        {
            string local = Path.GetFullPath(candidate, Path.Combine(m_candidate, sourceArtifactDirectory));
            if (File.Exists(local))
            {
                if (m_paths.comparer.Equals(PhysicalPathResolver.Resolve(local, m_paths), physical)) return candidate;
                continue;
            }
            string? resolved = Resolve(candidate, m_roots.Where(root => root.kind != "sdk-root").Select(root => root.path), m_paths);
            if (resolved is not null && m_paths.comparer.Equals(resolved, physical)) return candidate;
        }
        throw new IOException("A native header has no unambiguous portable include: '" + path
            + "'. Declare a source.includeDirectories root that preserves its relative identity.");
    }

    internal string IncludeArtifact(
        string artifact,
        string sourceArtifactDirectory
    )
        => m_paths.GetRelativePath(Path.Combine(m_candidate, sourceArtifactDirectory),
            Path.Combine(m_candidate, ArtifactDigestVerifier.ValidateRelativePath(artifact))).Replace('\\', '/');

    internal ImmutableArray<NativeInputRootBinding> Bindings(
        string targetId,
        string output
    )
        => m_roots.Where(root => root.kind != "generated").Select(root => new NativeInputRootBinding(targetId, root.id, root.path))
            .Append(new(targetId, "generated", output)).ToImmutableArray();

    internal NativeSourceManifest Manifest(
        string targetId,
        ImmutableArray<ArtifactDescriptor> artifacts,
        SourceSnapshot snapshot,
        SourceSnapshot declaredInputs,
        ImmutableArray<NativeShimPlan> shims,
        NativeBindingModule module,
        ImmutableArray<NativeExportBinding> exportBindings,
        NativeRuntimeRequirements? runtimeRequirements,
        ImmutableArray<CppLanguageRequirement> requirements,
        StaticExportTableDescriptor? staticTable,
        CancellationToken cancellationToken
    ) {
        HashSet<string> entries = m_request.headers.ToHashSet(m_paths.comparer);
        HashSet<string> shimSources = shims.SelectMany(shim => shim.sourceFiles).ToHashSet(m_paths.comparer);
        List<NativeInputFile> files = [];
        foreach (InputDependency input in snapshot.files.Concat(declaredInputs.files).DistinctBy(input => input.path, m_paths.comparer))
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Parser builtin headers belong to the parser, not the consumer's SDK.
            if (m_executionFiles.Contains(input.resolvedPath)) continue;
            var identity = Identity(input.path);
            files.Add(new(identity.Id, identity.Relative, input.length, input.digest,
                shimSources.Contains(input.path) ? "ShimSource" : entries.Contains(input.path) ? "EntryHeader" : "Dependency"));
        }
        HashSet<string> fileIds = files.Select(file => file.rootId + "/" + file.relativePath).ToHashSet(StringComparer.Ordinal);
        HashSet<string> generatedEntries = entries.Select(path => NativeGeneratedInputRelocation.MapPath(path, m_generated, m_paths))
            .Where(path => path is not null).Select(path => path!["artifact:/".Length..]).ToHashSet(StringComparer.Ordinal);
        foreach (ArtifactDescriptor artifact in artifacts.Where(artifact => artifact.relativePath.StartsWith(targetId + "/native/", StringComparison.Ordinal)
            && artifact.mediaType is "text/x-c" or "text/x-c++" or "text/x-c-header" or "text/x-c++-header"))
            if (fileIds.Add("generated/" + artifact.relativePath))
                files.Add(new("generated", artifact.relativePath, new FileInfo(Path.Combine(m_candidate, artifact.relativePath)).Length,
                    artifact.digest, generatedEntries.Contains(artifact.relativePath) ? "EntryHeader" : "Dependency"));
        ImmutableArray<NativeHeaderReference> references = m_request.headers.Select(path =>
        {
            var identity = Identity(path);
            string include = identity.Id == "generated" ? identity.Relative : Include(path, targetId + "/native/src");
            return new NativeHeaderReference(identity.Id, identity.Relative, include);
        }).ToImmutableArray();
        ImmutableArray<NativeIncludeDirectory> directories = m_roots.Where(root => root.kind != "sdk-root").Select(root =>
        {
            string? artifact = NativeGeneratedInputRelocation.MapPath(root.path, m_generated, m_paths);
            return new NativeIncludeDirectory(root.id, artifact is null ? string.Empty : artifact["artifact:/".Length..], root.system, root.framework);
        }).ToImmutableArray();
        return new(2, targetId, m_request.target.triple, m_request.language, m_request.languageStandard,
            artifacts.Where(artifact => artifact.relativePath.StartsWith(targetId + "/native/", StringComparison.Ordinal)
                && artifact.mediaType is "text/x-c" or "text/x-c++").ToImmutableArray(),
            artifacts.Where(artifact => artifact.relativePath.StartsWith(targetId + "/native/", StringComparison.Ordinal)
                && artifact.mediaType is "text/x-c-header" or "text/x-c++-header").ToImmutableArray(),
            m_roots.Where(root => root.kind != "generated").Select(root => new NativeInputRoot(root.id, root.kind))
                .Append(new("generated", "generated")).ToImmutableArray(),
            files.OrderBy(file => file.rootId, StringComparer.Ordinal).ThenBy(file => file.relativePath, StringComparer.Ordinal).ToImmutableArray(),
            directories, references, m_request.target.toolchain.defines.Concat(m_request.defines).Distinct(StringComparer.Ordinal).ToImmutableArray(),
            module, exportBindings, runtimeRequirements, requirements)
        { staticExportTable = staticTable };
    }

    private (string Id, string Relative) Identity(string path)
    {
        string full = Path.GetFullPath(path, m_request.pathBase);
        string? artifact = NativeGeneratedInputRelocation.MapPath(full, m_generated, m_paths);
        if (artifact is not null) return ("generated", artifact["artifact:/".Length..]);
        string physical = PhysicalPathResolver.Resolve(full, m_paths);
        Root? root = m_roots.Where(root => root.kind != "generated" && Within(full, root.path) && Within(physical, root.physical))
            .OrderByDescending(root => root.path.Length).ThenBy(root => root.id, StringComparer.Ordinal).FirstOrDefault();
        if (root is null) throw new IOException("A native dependency is outside every declared input root: '" + path + "'. Declare its source.includeDirectories or target SDK root.");
        return (root.id, ArtifactDigestVerifier.ValidateRelativePath(m_paths.GetRelativePath(root.path, full).Replace('\\', '/')));
    }

    private static IEnumerable<string> Candidates(
        string file,
        IEnumerable<string> roots,
        IHostPathService paths
    )
        => roots.Where(root => paths.IsWithin(file, root) && paths.IsWithin(PhysicalPathResolver.Resolve(file, paths), PhysicalPathResolver.Resolve(root, paths)))
            .Select(root => paths.GetRelativePath(root, file).Replace('\\', '/')).Distinct(StringComparer.Ordinal)
            .OrderBy(path => path.Length).ThenBy(path => path, StringComparer.Ordinal);

    private static bool CanResolve(
        string file,
        ImmutableArray<string> roots,
        IHostPathService paths
    )
        => Candidates(file, roots, paths).Any(candidate => Resolve(candidate, roots, paths) is string resolved && paths.Equals(resolved, PhysicalPathResolver.Resolve(file, paths)));

    private static string? Resolve(
        string include,
        IEnumerable<string> roots,
        IHostPathService paths
    ) {
        foreach (string root in roots)
        {
            string file = Path.GetFullPath(include, root);
            if (File.Exists(file)) return PhysicalPathResolver.Resolve(file, paths);
        }
        return null;
    }

    private bool Within(
        string file,
        string directory
    ) => m_paths.IsWithin(file, directory);
}
