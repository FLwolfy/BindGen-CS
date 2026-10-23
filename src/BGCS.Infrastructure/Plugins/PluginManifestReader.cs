using BGCS.Core.Hosting;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Plugins;
using BGCS.Infrastructure.IO;

namespace BGCS.Infrastructure.Plugins;

/// <summary>Retains an explicit plugin manifest and the immutable input closure captured during discovery.</summary>
/// <param name="path">The absolute manifest path.</param>
/// <param name="manifest">The strict declared plugin metadata.</param>
/// <param name="inputs">Manifest and executable file content identities.</param>
public sealed record LockedPluginManifest(
    string path,
    PluginManifest manifest,
    ImmutableArray<InputDependency> inputs
)
{
    /// <summary>Gets dependency metadata whose absence was part of discovery and must remain unchanged.</summary>
    public ImmutableArray<string> missingInputs => absentInputs.Select(input => input.path).ToImmutableArray();
    /// <summary>Gets the original physical identities of dependency metadata that was absent during discovery.</summary>
    public ImmutableArray<MissingInputDependency> absentInputs { get; init; } = [];
    /// <summary>Gets observed comparison metadata for the complete manifest closure and missing inputs.</summary>
    public ImmutableArray<InputPathSemantics> pathSemantics { get; init; } = [];
}

/// <summary>Reads explicit local plugin manifests and rejects undeclared or escaping executable inputs.</summary>
public sealed class PluginManifestReader
{
    private readonly IHostPathService m_paths;
    /// <summary>Captures the explicitly supplied host path service for executable closure validation.</summary>
    /// <param name="paths">The selected host path service.</param>
    public PluginManifestReader(IHostPathService paths) => m_paths = paths ?? throw new ArgumentNullException(nameof(paths));
    /// <summary>Locks metadata and every declared asset before an assembly is loaded.</summary>
    /// <param name="path">The local plugin manifest.</param>
    /// <returns>The immutable manifest and verified file identities.</returns>
    public LockedPluginManifest Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string absolute = Path.GetFullPath(path);
        byte[] bytes;
        using (FileStream manifestInput = new(absolute, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (manifestInput.Length > 4 * 1024 * 1024) throw new IOException("A plugin manifest exceeds the 4 MiB metadata limit.");
            bytes = new byte[checked((int)manifestInput.Length)];
            manifestInput.ReadExactly(bytes);
            if (manifestInput.ReadByte() != -1) throw new IOException("A plugin manifest changed while reading.");
        }
        JsonSerializerOptions options = new()
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            MaxDepth = 64,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
        };
        PluginManifest manifest = JsonSerializer.Deserialize<PluginManifest>(bytes, options) ?? throw new JsonException("A plugin manifest cannot be null.");
        if (manifest.schemaVersion != 1) throw new InvalidDataException("The plugin manifest revision is incompatible.");
        ArgumentNullException.ThrowIfNull(manifest.plugin);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.entryType);
        if (manifest.assets.IsDefaultOrEmpty) throw new InvalidDataException("A plugin must declare its complete executable file closure.");
        string root = Path.GetDirectoryName(absolute)!;
        string physicalRoot = PhysicalPathResolver.Resolve(root, m_paths);
        IEqualityComparer<string> comparer = m_paths.comparer;
        HashSet<string> paths = new(comparer);
        ImmutableArray<InputDependency>.Builder inputs = ImmutableArray.CreateBuilder<InputDependency>();
        inputs.Add(new(absolute, PhysicalPathResolver.Resolve(absolute, m_paths), bytes.Length, new(Convert.ToHexStringLower(SHA256.HashData(bytes)))));
        foreach (PluginAsset asset in manifest.assets)
        {
            ArgumentNullException.ThrowIfNull(asset);
            string assetPath = ResolveAsset(root, asset.path, m_paths);
            if (!paths.Add(assetPath)) throw new InvalidDataException("A plugin manifest declares duplicate asset paths.");
            string physical = PhysicalPathResolver.Resolve(assetPath, m_paths);
            if (!m_paths.IsWithin(physical, physicalRoot, includeRoot: false)) throw new InvalidDataException("A plugin asset escapes its manifest directory through a link.");
            using FileStream stream = File.OpenRead(assetPath);
            if (stream.Length > 256 * 1024 * 1024) throw new IOException("A plugin asset exceeds the 256 MiB activation limit.");
            ContentDigest digest = new(Convert.ToHexStringLower(SHA256.HashData(stream)));
            if (digest != asset.digest) throw new InvalidDataException($"Plugin asset '{asset.path}' does not match its locked content identity.");
            inputs.Add(new(assetPath, physical, stream.Length, digest));
        }
        string entry = ResolveAsset(root, manifest.assembly, m_paths);
        if (!entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || !paths.Contains(entry)) throw new InvalidDataException("The plugin entry assembly is absent from its locked asset closure.");
        string dependencies = Path.ChangeExtension(entry, ".deps.json");
        if (File.Exists(dependencies) && !paths.Contains(dependencies)) throw new InvalidDataException("Existing plugin dependency metadata must be included in the locked asset closure.");
        ImmutableArray<string> missing = File.Exists(dependencies) ? [] : [dependencies];
        SourceSnapshot snapshot = InputPathSemanticsSnapshot.Capture(new(inputs.ToImmutable(), [])
        { absentInputs = missing.Select(path => new MissingInputDependency(path, PhysicalPathResolver.Resolve(path, m_paths))).ToImmutableArray() }, m_paths);
        if (SourceSnapshotVerifier.GetMismatch(snapshot, m_paths) is string mismatch) throw new IOException("Plugin inputs changed during discovery. " + mismatch);
        return new(absolute, manifest, snapshot.files) { absentInputs = snapshot.absentInputs, pathSemantics = snapshot.pathSemantics };
    }

    /// <summary>Rechecks a frozen manifest closure before activation or publication.</summary>
    /// <param name="manifest">The captured plugin input closure.</param>
    public void Verify(LockedPluginManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        SourceSnapshot snapshot = new(manifest.inputs, [])
        {
            absentInputs = manifest.absentInputs,
            pathSemantics = manifest.pathSemantics
        };
        if (SourceSnapshotVerifier.GetMismatch(snapshot, m_paths) is string mismatch) throw new IOException("Plugin inputs changed after discovery. " + mismatch);
    }

    internal static string ResolveAsset(
        string root,
        string path,
        IHostPathService paths
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Path.IsPathRooted(path) || path.Contains('\\') || path.Contains(':') || path.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new InvalidDataException("A plugin asset requires a portable relative path without parent traversal.");
        string full = Path.GetFullPath(Path.Combine(root, path));
        if (!paths.IsWithin(full, root, includeRoot: false)) throw new InvalidDataException("A plugin asset escapes its manifest directory.");
        return full;
    }


}
