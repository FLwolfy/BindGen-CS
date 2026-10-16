using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Requests;
using BGCS.Infrastructure.IO;

namespace BGCS;

internal sealed record NativeGeneratedDirectoryMapping(
    string virtualId,
    string sourceDirectory,
    string artifactDirectory,
    string? logicalSourcePrefix = null
);
internal sealed record NativeGeneratedInputMap(
    string storeRoot,
    ImmutableArray<NativeGeneratedDirectoryMapping> directories
);

internal sealed class NativeGeneratedInputRelocation
{
    private static readonly StringComparer s_paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly StringComparison s_pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private readonly ImmutableArray<NativeGeneratedInputMap> m_maps;
    private readonly Dictionary<string, (string Artifact, long Length, ContentDigest Digest, string? Logical)> m_files = new(s_paths);
    internal ImmutableArray<NativeGeneratedDirectoryContract> contracts { get; }

    internal NativeGeneratedInputRelocation(
        ImmutableArray<NativeGeneratedInputMap> maps,
        ImmutableArray<ArtifactDescriptor> artifacts,
        string? artifactRoot
    ) {
        m_maps = maps.IsDefault ? [] : maps;
        if (m_maps.IsEmpty) { contracts = []; return; }
        if (artifactRoot is null || !Path.IsPathFullyQualified(artifactRoot)) throw new InvalidOperationException("Generated input relocation requires its exact candidate artifact root.");
        Dictionary<string, ArtifactDescriptor> declared = artifacts.ToDictionary(artifact => artifact.relativePath, s_paths);
        Dictionary<string, string?> logicalSources = new(s_paths);
        ImmutableArray<NativeGeneratedDirectoryContract>.Builder directories = ImmutableArray.CreateBuilder<NativeGeneratedDirectoryContract>();
        HashSet<string> destinations = new(s_paths), identities = new(StringComparer.Ordinal);
        HashSet<string> logicalIdentities = new(StringComparer.Ordinal);
        foreach (NativeGeneratedInputMap map in m_maps)
        {
            if (!Path.IsPathFullyQualified(map.storeRoot) || map.directories.IsDefaultOrEmpty || !Directory.Exists(map.storeRoot))
                throw new InvalidOperationException("A generated input store requires complete absolute directory mappings.");
            Dictionary<string, string> sourceFiles = new(s_paths);
            foreach (NativeGeneratedDirectoryMapping directory in map.directories)
            {
                Portable(directory.artifactDirectory);
                if (string.IsNullOrWhiteSpace(directory.virtualId) || !identities.Add(directory.virtualId) || !destinations.Add(directory.artifactDirectory)
                    || !Path.IsPathFullyQualified(directory.sourceDirectory) || !Within(directory.sourceDirectory, map.storeRoot) || !Directory.Exists(directory.sourceDirectory))
                    throw new InvalidOperationException("Generated input role directories require unique stable identities and contained absolute origins.");
                foreach (string file in EnumerateFiles(directory.sourceDirectory))
                {
                    string relative = Path.GetRelativePath(directory.sourceDirectory, file).Replace('\\', '/');
                    string destination = directory.artifactDirectory + "/" + relative;
                    if (!declared.TryGetValue(destination, out ArtifactDescriptor? descriptor)) throw new InvalidOperationException("A generated source input has no published artifact lock: " + destination);
                    NativeGeneratedFileContract captured = Capture(file, relative);
                    string? logical = directory.logicalSourcePrefix is null ? null : directory.logicalSourcePrefix + relative;
                    if (logical is not null)
                    {
                        Portable(logical);
                        if (!logicalIdentities.Add(logical)) throw new InvalidOperationException("Two generated physical inputs cannot claim the same logical source identity.");
                    }
                    if (captured.digest != descriptor.digest || !sourceFiles.TryAdd(Path.GetFullPath(file), destination)
                        || !m_files.TryAdd(Path.GetFullPath(file), ("artifact:/" + destination, captured.length, captured.digest, logical)) || !logicalSources.TryAdd(destination, logical))
                        throw new InvalidOperationException("A generated input changed content or overlaps another relocation: " + destination);
                }
                ImmutableArray<NativeGeneratedFileContract> members = declared.Values.Where(artifact => artifact.relativePath.StartsWith(directory.artifactDirectory + "/", StringComparison.Ordinal))
                    .OrderBy(artifact => artifact.relativePath, StringComparer.Ordinal).Select(artifact =>
                    {
                        string relative = artifact.relativePath[(directory.artifactDirectory.Length + 1)..];
                        string path = NativeBuildManifestLoader.Resolve("artifact:/" + artifact.relativePath, artifactRoot);
                        NativeGeneratedFileContract captured = Capture(path, relative);
                        if (captured.digest != artifact.digest) throw new IOException("A published generated input differs from its declared digest: " + artifact.relativePath);
                        logicalSources.TryGetValue(artifact.relativePath, out string? logical);
                        return captured with { logicalSourcePath = logical };
                    }).ToImmutableArray();
                string destinationRoot = NativeBuildManifestLoader.Resolve("artifact:/" + directory.artifactDirectory, artifactRoot);
                ImmutableArray<string> publishedMembers = EnumerateFiles(destinationRoot);
                HashSet<string> expectedMembers = members.Select(member => member.relativePath).ToHashSet(s_paths);
                if (publishedMembers.Length != expectedMembers.Count || publishedMembers.Any(path => !expectedMembers.Contains(Path.GetRelativePath(destinationRoot, path).Replace('\\', '/'))))
                    throw new IOException("The generated candidate role contains undeclared files: " + directory.artifactDirectory);
                directories.Add(new(directory.virtualId, "artifact:/" + directory.artifactDirectory, members));
            }
            string[] actualFiles = EnumerateFiles(map.storeRoot).Select(Path.GetFullPath).ToArray();
            if (actualFiles.Length != sourceFiles.Count || actualFiles.Any(file => !sourceFiles.ContainsKey(file)))
                throw new InvalidOperationException("A generated input store contains an unmapped file; its original path cannot be discarded.");
        }
        NativeGeneratedDirectoryContract[] values = directories.ToArray();
        for (int left = 0; left < values.Length; left++)
            for (int right = left + 1; right < values.Length; right++)
                if (values[left].path.StartsWith(values[right].path + "/", s_pathComparison) || values[right].path.StartsWith(values[left].path + "/", s_pathComparison))
                    throw new InvalidOperationException("Generated input destination role directories cannot overlap.");
        contracts = values.ToImmutableArray();
    }

    internal InputDependency Relocate(InputDependency input)
    {
        if (m_files.TryGetValue(Path.GetFullPath(input.path), out var file))
        {
            if (!s_paths.Equals(PhysicalPathResolver.Resolve(input.path), input.resolvedPath) || file.Length != input.length || file.Digest != input.digest)
                throw new IOException("A generated input lock differs from its verified published artifact: " + input.path);
            return input with { path = file.Artifact, resolvedPath = file.Artifact };
        }
        if (InsideStore(input.path)) throw new InvalidOperationException("An unmapped generated input cannot remain an external manifest dependency: " + input.path);
        return input;
    }

    internal string? RelocatePath(string path) => MapPath(path, m_maps);

    internal ImmutableDictionary<string, string> SourcePathMappings(string artifactRoot)
        => contracts.SelectMany(directory => directory.files.Where(file => file.logicalSourcePath is not null)
            .Select(file => new KeyValuePair<string, string>(NativeBuildManifestLoader.Resolve(directory.path + "/" + file.relativePath, artifactRoot), file.logicalSourcePath!)))
            .ToImmutableDictionary(s_paths);

    internal SourceSnapshot Relocate(SourceSnapshot snapshot)
    {
        ImmutableArray<DirectoryInputDependency>.Builder directories = ImmutableArray.CreateBuilder<DirectoryInputDependency>();
        foreach (DirectoryInputDependency directory in snapshot.searchDirectories)
        {
            if (MapPath(directory.path, m_maps) is null)
            {
                if (InsideStore(directory.path)) throw new InvalidOperationException("A generated input search root has no exact published role mapping: " + directory.path);
                directories.Add(directory);
            }
        }
        return snapshot with
        {
            files = snapshot.files.Select(Relocate).ToImmutableArray(),
            searchDirectories = directories.ToImmutable(),
            absentInputs = snapshot.absentInputs.Select(input =>
            {
                string? relocated = MapPath(input.path, m_maps);
                if (relocated is null)
                {
                    if (InsideStore(input.path)) throw new InvalidOperationException("A generated absent input has no portable role origin.");
                    return input;
                }
                if (m_files.ContainsKey(Path.GetFullPath(input.path))) throw new InvalidOperationException("Generated input relocation cannot preserve a contradictory absence lock.");
                return input with { path = relocated, resolvedPath = relocated };
            }).ToImmutableArray(),
            excludedDirectories = snapshot.excludedDirectories.Select(path => MapPath(path, m_maps) ?? (InsideStore(path)
                ? throw new InvalidOperationException("A generated input exclusion has no portable role origin.") : path)).ToImmutableArray()
        };
    }

    internal static string? MapPath(
        string path,
        ImmutableArray<NativeGeneratedInputMap> maps
    ) {
        if (maps.IsDefaultOrEmpty) return null;
        foreach (NativeGeneratedDirectoryMapping directory in maps.SelectMany(map => map.directories))
        {
            if (!Within(path, directory.sourceDirectory)) continue;
            string relative = Path.GetRelativePath(directory.sourceDirectory, path).Replace('\\', '/');
            return "artifact:/" + directory.artifactDirectory + (relative == "." ? string.Empty : "/" + relative);
        }
        return null;
    }

    internal static string HeaderInclude(
        string path,
        ImmutableArray<NativeGeneratedInputMap> maps,
        string sourceArtifactDirectory
    ) {
        string? relocated = MapPath(path, maps);
        string anchor = Path.GetPathRoot(AppContext.BaseDirectory)!;
        return relocated is null ? path : Path.GetRelativePath(Path.GetFullPath(sourceArtifactDirectory, anchor), Path.GetFullPath(relocated["artifact:/".Length..], anchor)).Replace('\\', '/');
    }

    internal static ImmutableArray<string> EnumerateFiles(string directory)
    {
        ImmutableArray<string>.Builder files = ImmutableArray.CreateBuilder<string>();
        Stack<string> pending = new(); pending.Push(directory);
        while (pending.TryPop(out string? current))
        {
            DirectoryInfo root = new(current);
            if (root.LinkTarget is not null || (root.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Generated input directories cannot contain symbolic links or reparse points.");
            FileSystemInfo[] entries = root.EnumerateFileSystemInfos().OrderBy(entry => entry.Name, StringComparer.Ordinal).ToArray();
            if (entries.Length == 0 && current != directory) throw new IOException("Generated input directories cannot contain undeclared empty directories.");
            foreach (FileSystemInfo entry in entries)
            {
                if (entry.LinkTarget is not null || (entry.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Generated inputs cannot contain symbolic links or reparse points.");
                if ((entry.Attributes & FileAttributes.Directory) != 0) pending.Push(entry.FullName); else files.Add(entry.FullName);
            }
        }
        return files.ToImmutable();
    }
    private bool InsideStore(string path) => m_maps.Any(map => Within(path, map.storeRoot));
    private static bool Within(
        string path,
        string root
    ) => s_paths.Equals(Path.GetFullPath(path), Path.GetFullPath(root))
        || Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, s_pathComparison);
    internal static void Portable(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || path.Contains(':') || Path.IsPathRooted(path) || path.Split('/').Any(part => part is "" or "." or ".."))
            throw new InvalidDataException("A generated input member must have a portable contained relative path.");
    }
    internal static NativeGeneratedFileContract Capture(
        string path,
        string relative
    ) {
        Portable(relative);
        using FileStream stream = File.OpenRead(path);
        return new(relative, stream.Length, new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(stream))));
    }
}
