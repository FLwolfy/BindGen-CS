using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Plugins;
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
    public ImmutableArray<string> missingInputs { get; init; } = [];
}

/// <summary>Reads explicit local plugin manifests and rejects undeclared or escaping executable inputs.</summary>
public sealed class PluginManifestReader
{
    /// <summary>Locks metadata and every declared asset before an assembly is loaded.</summary>
    /// <param name="path">The local plugin manifest.</param>
    /// <returns>The immutable manifest and verified file identities.</returns>
    public LockedPluginManifest Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string absolute = Path.GetFullPath(path);
        byte[] bytes = File.ReadAllBytes(absolute);
        if (bytes.Length > 4 * 1024 * 1024) throw new IOException("A plugin manifest exceeds the 4 MiB metadata limit.");
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
        string physicalRoot = PhysicalPathResolver.Resolve(root);
        StringComparer comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        HashSet<string> paths = new(comparer);
        ImmutableArray<InputDependency>.Builder inputs = ImmutableArray.CreateBuilder<InputDependency>();
        inputs.Add(new(absolute, PhysicalPathResolver.Resolve(absolute), bytes.Length, new(Convert.ToHexStringLower(SHA256.HashData(bytes)))));
        foreach (PluginAsset asset in manifest.assets)
        {
            ArgumentNullException.ThrowIfNull(asset);
            string assetPath = ResolveAsset(root, asset.path);
            if (!paths.Add(assetPath)) throw new InvalidDataException("A plugin manifest declares duplicate asset paths.");
            string physical = PhysicalPathResolver.Resolve(assetPath);
            if (!Contained(physicalRoot, physical)) throw new InvalidDataException("A plugin asset escapes its manifest directory through a link.");
            using FileStream stream = File.OpenRead(assetPath);
            if (stream.Length > 256 * 1024 * 1024) throw new IOException("A plugin asset exceeds the 256 MiB activation limit.");
            ContentDigest digest = new(Convert.ToHexStringLower(SHA256.HashData(stream)));
            if (digest != asset.digest) throw new InvalidDataException($"Plugin asset '{asset.path}' does not match its locked content identity.");
            inputs.Add(new(assetPath, physical, stream.Length, digest));
        }
        string entry = ResolveAsset(root, manifest.assembly);
        if (!entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || !paths.Contains(entry)) throw new InvalidDataException("The plugin entry assembly is absent from its locked asset closure.");
        string dependencies = Path.ChangeExtension(entry, ".deps.json");
        if (File.Exists(dependencies) && !paths.Contains(dependencies)) throw new InvalidDataException("Existing plugin dependency metadata must be included in the locked asset closure.");
        return new(absolute, manifest, inputs.ToImmutable()) { missingInputs = File.Exists(dependencies) ? [] : [dependencies] };
    }

    /// <summary>Rechecks a frozen manifest closure before activation or publication.</summary>
    /// <param name="manifest">The captured plugin input closure.</param>
    public void Verify(LockedPluginManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        foreach (string absent in manifest.missingInputs)
            if (File.Exists(absent) || Directory.Exists(absent)) throw new IOException("Plugin dependency metadata appeared after discovery; create a new request or host.");
        foreach (InputDependency input in manifest.inputs)
        {
            if (PhysicalPathResolver.Resolve(input.path) != input.resolvedPath) throw new IOException("A plugin input changed its physical identity after discovery.");
            using FileStream stream = File.OpenRead(input.path);
            if (stream.Length != input.length || new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(stream))) != input.digest)
                throw new IOException("A plugin input changed after discovery; create a new request or host for the new implementation.");
        }
    }

    internal static string ResolveAsset(
        string root,
        string path
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Path.IsPathRooted(path) || path.Contains('\\') || path.Contains(':') || path.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new InvalidDataException("A plugin asset requires a portable relative path without parent traversal.");
        string full = Path.GetFullPath(Path.Combine(root, path));
        if (!Contained(root, full)) throw new InvalidDataException("A plugin asset escapes its manifest directory.");
        return full;
    }

    private static bool Contained(
        string root,
        string path
    )
        => path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
