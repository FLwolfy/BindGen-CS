using System.Collections.Immutable;
using System.Text.Json.Serialization;
using BGCS.Core.Common;

namespace BGCS.Core.Plugins;

/// <summary>Declares an explicit plugin entry and its complete executable file closure.</summary>
public sealed record PluginManifest
{
    /// <summary>Gets the explicit manifest format revision.</summary>
    [JsonRequired]
    public int schemaVersion { get; init; }
    /// <summary>Gets the exact metadata expected from the public plugin entry.</summary>
    [JsonRequired]
    public PluginDescriptor plugin { get; init; } = null!;
    /// <summary>Gets the portable relative entry assembly path.</summary>
    [JsonRequired]
    public string assembly { get; init; } = string.Empty;
    /// <summary>Gets the exact public entry type name, without automatic assembly-wide discovery.</summary>
    [JsonRequired]
    public string entryType { get; init; } = string.Empty;
    /// <summary>Gets whether activation is restricted to a parser worker.</summary>
    public bool workerOnly { get; init; }
    /// <summary>Gets the exact parser implementation identity required by a worker-only extension; ordinary plugins leave it null.</summary>
    public string? workerParserIdentity { get; init; }
    /// <summary>Gets all managed, native and dependency-manifest files permitted during activation.</summary>
    [JsonRequired]
    public ImmutableArray<PluginAsset> assets { get; init; } = [];
}

/// <summary>Locks one portable manifest-relative plugin file to exact content.</summary>
/// <param name="path">The manifest-relative asset path.</param>
/// <param name="digest">The required content identity.</param>
public sealed record PluginAsset(
    string path,
    ContentDigest digest
);
