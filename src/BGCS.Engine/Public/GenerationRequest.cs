using System;
using System.IO;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core.Hosting;
using BGCS.Core.Inputs;
using BGCS.Infrastructure.IO;
using BGCS.Infrastructure.Plugins;

namespace BGCS;

/// <summary>Describes generation inputs; file-backed inputs are read only after the caller's host services have been selected.</summary>
public sealed record GenerationRequest
{
    private readonly BindingConfiguration? m_configuration;
    private readonly string? m_profile;
    private bool materialized { get; init; }

    /// <summary>Freezes programmatic generation choices without reading plugin manifests or executing a frontend.</summary>
    /// <param name="configuration">The immutable grouped generation choices.</param>
    /// <param name="baseDirectory">The captured base for relative local inputs.</param>
    public GenerationRequest(
        BindingConfiguration configuration,
        string baseDirectory
    ) {
        m_configuration = BindingConfigurationLoader.ValidateAndFreeze(configuration);
        this.baseDirectory = Path.GetFullPath(baseDirectory);
    }

    private GenerationRequest(
        string path,
        string? profile
    ) {
        configurationPath = Path.GetFullPath(path);
        baseDirectory = Path.GetDirectoryName(configurationPath)!;
        m_profile = profile;
    }

    /// <summary>Gets frozen configuration after host-aware materialization; a file descriptor has no effective configuration before then.</summary>
    public BindingConfiguration configuration => m_configuration ?? throw new InvalidOperationException("File-backed generation configuration is available only after the selected host materializes its inputs.");
    /// <summary>Gets the captured configuration directory.</summary>
    public string baseDirectory { get; }
    /// <summary>Gets the originating local configuration path, or null for programmatic inputs.</summary>
    public string? configurationPath { get; private init; }
    /// <summary>Gets the immutable composed configuration input closure and host comparison metadata.</summary>
    public SourceSnapshot configurationSnapshot { get; private init; } = new([], []);
    /// <summary>Gets independently verifiable executable input snapshots for request-declared plugins.</summary>
    public ImmutableArray<SourceSnapshot> pluginSnapshots { get; private init; } = [];
    internal ImmutableArray<LockedPluginManifest> plugins { get; private init; } = [];
    internal string? previewOutputRoot { get; private init; }

    /// <summary>Captures a local configuration descriptor without reading it or choosing host services.</summary>
    /// <param name="path">The local configuration path.</param>
    /// <param name="profile">An explicit named preset selection.</param>
    /// <returns>A descriptor that the selected host materializes at session startup.</returns>
    public static GenerationRequest FromFile(
        string path,
        string? profile = null
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new(path, profile);
    }

    /// <summary>Reads and freezes local configuration and plugin inputs using host facilities already selected by the caller.</summary>
    /// <param name="paths">The explicitly selected host path service; configuration cannot replace it.</param>
    /// <param name="cancellationToken">Cancels bounded local reads and input verification.</param>
    /// <returns>The materialized immutable generation request and independently checkable provenance.</returns>
    public async ValueTask<GenerationRequest> MaterializeAsync(
        IHostPathService paths,
        CancellationToken cancellationToken
    ) {
        ArgumentNullException.ThrowIfNull(paths);
        cancellationToken.ThrowIfCancellationRequested();
        if (materialized)
        {
            if (SourceSnapshotVerifier.GetMismatch(configurationSnapshot, paths, cancellationToken) is string mismatch)
                throw new IOException("Configuration inputs changed after capture. " + mismatch);
            foreach (LockedPluginManifest manifest in plugins) new PluginManifestReader(paths).Verify(manifest);
            return this;
        }
        ConfigurationLoadResult? loaded = configurationPath is null || m_configuration is not null ? null
            : await new BindingConfigurationLoader(paths).LoadWithInputsAsync(configurationPath, m_profile, cancellationToken).ConfigureAwait(false);
        BindingConfiguration effective = m_configuration ?? loaded!.configuration;
        PluginManifestReader reader = new(paths);
        ImmutableArray<LockedPluginManifest> locked = effective.extensions.plugins.Select(path => reader.Read(Path.GetFullPath(path, baseDirectory))).ToImmutableArray();
        GenerationRequest result = new(effective, baseDirectory)
        {
            configurationPath = configurationPath,
            configurationSnapshot = loaded?.snapshot ?? configurationSnapshot,
            plugins = locked,
            pluginSnapshots = locked.Select(manifest => new SourceSnapshot(manifest.inputs, [])
            {
                absentInputs = manifest.absentInputs,
                pathSemantics = manifest.pathSemantics
            }).ToImmutableArray(),
            materialized = true,
            previewOutputRoot = previewOutputRoot
        };
        if (SourceSnapshotVerifier.GetMismatch(result.configurationSnapshot, paths, cancellationToken) is string changed)
            throw new IOException("Configuration inputs changed during materialization. " + changed);
        foreach (SourceSnapshot snapshot in result.pluginSnapshots)
            if (SourceSnapshotVerifier.GetMismatch(snapshot, paths, cancellationToken) is string pluginChanged)
                throw new IOException("Plugin inputs changed during materialization. " + pluginChanged);
        return result;
    }

    /// <summary>Applies immutable overrides to materialized or programmatic configuration while retaining input provenance.</summary>
    /// <param name="configuration">The replacement complete configuration.</param>
    /// <returns>A descriptor whose plugin closure is recaptured under the session's host services.</returns>
    public GenerationRequest WithConfiguration(BindingConfiguration configuration) => new(configuration, baseDirectory)
    {
        configurationPath = configurationPath,
        configurationSnapshot = configurationSnapshot,
        previewOutputRoot = previewOutputRoot
    };

    internal GenerationRequest WithPreviewOutput(string outputRoot) => this with { previewOutputRoot = Path.GetFullPath(outputRoot) };

    internal GenerationRequest WithAdditionalInputs(
        SourceSnapshot snapshot,
        IHostPathService paths
    ) {
        if (SourceSnapshotVerifier.GetMismatch(configurationSnapshot, paths) is string oldMismatch) throw new IOException("Existing configuration provenance changed. " + oldMismatch);
        if (SourceSnapshotVerifier.GetMismatch(snapshot, paths) is string addedMismatch) throw new IOException("Additional configuration provenance changed. " + addedMismatch);
        SourceSnapshot combined = InputPathSemanticsSnapshot.Capture(new(
            configurationSnapshot.files.AddRange(snapshot.files).DistinctBy(input => input.path, paths.comparer).ToImmutableArray(),
            configurationSnapshot.searchDirectories.AddRange(snapshot.searchDirectories))
        { absentInputs = configurationSnapshot.absentInputs.AddRange(snapshot.absentInputs) }, paths);
        return this with { configurationSnapshot = combined };
    }
}
