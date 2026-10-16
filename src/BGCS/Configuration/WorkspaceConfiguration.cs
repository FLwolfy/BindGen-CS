using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace BGCS.Configuration;

/// <summary>Declares a strict local workspace graph before project configuration loading.</summary>
public sealed record WorkspaceConfiguration
{
    /// <summary>Gets the optional editor schema reference.</summary>
    [JsonPropertyName("$schema")]
    public string? schema { get; init; }
    /// <summary>Gets the explicitly declared workspace format revision.</summary>
    [JsonRequired]
    public int schemaVersion { get; init; }
    /// <summary>Gets the maximum simultaneously executing project count.</summary>
    public int parallelism { get; init; } = 2;
    /// <summary>Gets the complete project graph in presentation order.</summary>
    public ImmutableArray<WorkspaceProjectConfiguration> projects { get; init; } = [];
}

/// <summary>Declares one project's local configuration and explicit prerequisites.</summary>
public sealed record WorkspaceProjectConfiguration
{
    /// <summary>Gets the unique ordinal project identity.</summary>
    public string id { get; init; } = string.Empty;
    /// <summary>Gets the workspace-relative grouped configuration path.</summary>
    public string configuration { get; init; } = string.Empty;
    /// <summary>Gets an optional explicit configuration profile selection.</summary>
    public string? profile { get; init; }
    /// <summary>Gets project identities that must complete successfully before this project executes.</summary>
    public ImmutableArray<string> dependencies { get; init; } = [];
    /// <summary>Gets a subset of target identities already declared in this project's configuration.</summary>
    public ImmutableArray<string> targets { get; init; } = [];
}
