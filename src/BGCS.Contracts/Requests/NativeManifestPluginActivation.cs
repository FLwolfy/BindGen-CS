using System;
using System.Collections.Immutable;
using System.Text.Json;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Plugins;

namespace BGCS.Contracts.Requests;

/// <summary>Freezes plugin settings without evaluating a computed path base that may have multiple origins.</summary>
public sealed record NativeManifestPluginSettings
{
    /// <summary>Clones one complete plugin settings namespace and its original local input origins.</summary>
    /// <param name="id">The owning plugin identity.</param>
    /// <param name="settings">The complete frozen plugin settings object.</param>
    /// <param name="defaultPathBase">The absolute fallback input base.</param>
    /// <param name="valuePathBases">Immutable origins for composed JSON-pointer settings.</param>
    public NativeManifestPluginSettings(
        string id,
        JsonElement settings,
        string defaultPathBase,
        ImmutableDictionary<string, string> valuePathBases
    ) {
        PluginConfiguration validated = new(id, settings, defaultPathBase, valuePathBases);
        this.id = validated.id;
        this.settings = validated.settings;
        this.defaultPathBase = validated.defaultPathBase;
        this.valuePathBases = validated.valuePathBases;
    }
    /// <summary>Gets the owning plugin identity.</summary>
    public string id { get; }
    /// <summary>Gets the detached settings object.</summary>
    public JsonElement settings { get; }
    /// <summary>Gets the explicitly captured fallback origin.</summary>
    public string defaultPathBase { get; }
    /// <summary>Gets immutable per-value origins.</summary>
    public ImmutableDictionary<string, string> valuePathBases { get; }
}

/// <summary>Records portable plugin activation or an explicit matching-host requirement for a factory-only SDK plugin.</summary>
/// <param name="manifestPath">The absolute locked manifest, or null when the caller must provide the same host registration.</param>
/// <param name="descriptor">The exact frozen compatibility, service and settings-schema contract.</param>
/// <param name="configuration">The original independently owned plugin settings.</param>
/// <param name="inputs">Complete content identities for the manifest and executable assets.</param>
/// <param name="missingInputs">Dependency metadata whose absence was part of activation.</param>
public sealed record NativeManifestPluginActivation(
    string? manifestPath,
    PluginDescriptor descriptor,
    NativeManifestPluginSettings configuration,
    ImmutableArray<InputDependency> inputs,
    ImmutableArray<MissingInputDependency> missingInputs
);
