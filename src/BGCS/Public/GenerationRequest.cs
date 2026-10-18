using System;
using System.IO;
using System.Collections.Immutable;
using BGCS.Configuration;
using BGCS.Contracts.Inputs;
using BGCS.Infrastructure.Plugins;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BGCS;

/// <summary>Captures grouped configuration and its relative-path base before a generation session begins.</summary>
public sealed record GenerationRequest
{
    /// <summary>Captures a validated SDK configuration and an explicit path base.</summary>
    /// <param name="configuration">The immutable grouped generation choices.</param>
    /// <param name="baseDirectory">The captured base for every relative local input.</param>
    public GenerationRequest(
        BindingConfiguration configuration,
        string baseDirectory
    ) {
        this.configuration = new BindingConfigurationLoader().ValidateAndFreeze(configuration);
        this.baseDirectory = Path.GetFullPath(baseDirectory);
        plugins = this.configuration.extensions.plugins.Select(path => new PluginManifestReader().Read(Path.GetFullPath(path, this.baseDirectory))).ToImmutableArray();
    }

    /// <summary>Gets the frozen effective generation configuration.</summary>
    public BindingConfiguration configuration { get; }
    /// <summary>Gets the captured configuration directory.</summary>
    public string baseDirectory { get; }
    /// <summary>Gets the originating local configuration path, or null for SDK-created requests.</summary>
    public string? configurationPath { get; private init; }
    /// <summary>Gets immutable content identities for the complete composed configuration closure.</summary>
    public ImmutableArray<InputDependency> configurationInputs { get; private init; } = [];
    /// <summary>Gets the executable input closure of all request-declared plugins.</summary>
    public ImmutableArray<InputDependency> pluginInputs => plugins.SelectMany(plugin => plugin.inputs).ToImmutableArray();
    internal ImmutableArray<LockedPluginManifest> plugins { get; }
    internal string? previewOutputRoot { get; private init; }

    /// <summary>Reads the new configuration and resolves relative paths from its containing directory.</summary>
    /// <param name="path">The local bindgen.json input.</param>
    /// <param name="profile">An optional explicit named preset selection.</param>
    /// <param name="cancellationToken">Cancels composition, local reads and composition.</param>
    /// <param name="configurationLoader">An optional loader with a caller-owned HTTPS transport.</param>
    /// <returns>The independently frozen request.</returns>
    public static GenerationRequest FromFile(
        string path,
        string? profile = null,
        CancellationToken cancellationToken = default,
        BindingConfigurationLoader? configurationLoader = null
    )
        => FromFileAsync(path, profile, cancellationToken, configurationLoader).GetAwaiter().GetResult();

    /// <summary>Asynchronously captures a local root, its local bases and exact input provenance.</summary>
    /// <param name="path">The local bindgen.json input.</param>
    /// <param name="profile">An optional explicit preset selection.</param>
    /// <param name="cancellationToken">Cancels file/network reads and composition.</param>
    /// <param name="configurationLoader">An optional loader with a caller-owned HTTPS transport.</param>
    /// <returns>The independently frozen request with local content identities.</returns>
    public static async Task<GenerationRequest> FromFileAsync(
        string path,
        string? profile = null,
        CancellationToken cancellationToken = default,
        BindingConfigurationLoader? configurationLoader = null
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string absolute = Path.GetFullPath(path);
        ConfigurationLoadResult loaded = await (configurationLoader ?? new()).LoadWithInputsAsync(absolute, profile, cancellationToken).ConfigureAwait(false);
        return new(loaded.configuration, Path.GetDirectoryName(absolute)!)
        { configurationPath = absolute, configurationInputs = loaded.inputs };
    }

    /// <summary>Applies immutable SDK or integration overrides while retaining the originating input and path base.</summary>
    /// <param name="configuration">The replacement validated configuration snapshot.</param>
    /// <returns>A request with the same input provenance.</returns>
    public GenerationRequest WithConfiguration(BindingConfiguration configuration)
        => new(configuration, baseDirectory)
        {
            configurationPath = this.configurationPath,
            configurationInputs = this.configurationInputs,
            previewOutputRoot = this.previewOutputRoot
        };

    internal GenerationRequest WithPreviewOutput(string outputRoot)
        => this with { previewOutputRoot = Path.GetFullPath(outputRoot) };

    internal GenerationRequest WithAdditionalInputs(ImmutableArray<InputDependency> inputs)
        => this with { configurationInputs = configurationInputs.AddRange(inputs).DistinctBy(input => input.path, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToImmutableArray() };
}
