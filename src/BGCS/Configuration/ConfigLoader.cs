using System;
using Newtonsoft.Json;

namespace BGCS.Configuration;

/// <summary>
/// Loads composed generator configuration without rewriting an existing source document.
/// </summary>
public sealed class ConfigLoader
{
    private readonly IConfigComposer? m_composer;
    private readonly PresetResolver m_presets;
    /// <summary>
    /// Creates a loader with the selected composition and preset policies.
    /// </summary>
    /// <param name="composer">
    /// Optional custom composer; default composition preserves explicitly supplied JSON values.
    /// </param>
    /// <param name="presets">
    /// Optional named preset catalog; the built-in catalog is used when omitted.
    /// </param>
    public ConfigLoader(
        IConfigComposer? composer = null,
        PresetResolver? presets = null
    ) {
        this.m_composer = composer;
        this.m_presets = presets ?? PresetResolver.@default;
    }

    /// <summary>
    /// Loads, composes and validates configuration, then registers explicitly requested plugins.
    /// </summary>
    /// <param name="path">
    /// Path to an existing configuration document.
    /// </param>
    /// <returns>
    /// A fresh configuration whose relative paths use its document directory.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// The configuration path is empty or contains only whitespace.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Composition, validation or plugin registration fails.
    /// </exception>
    /// <exception cref="System.IO.FileNotFoundException">
    /// The configuration document does not exist.
    /// </exception>
    public CsCodeGeneratorConfig Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A configuration path is required.", nameof(path));
        CsCodeGeneratorConfig config;
        if (this.m_composer == null)
        {
            config = new ConfigDocumentLoader(this.m_presets).Load(path);
        }
        else
        {
            string fullPath = System.IO.Path.GetFullPath(path);
            if (!System.IO.File.Exists(fullPath))
                throw new System.IO.FileNotFoundException($"Configuration file not found: {fullPath}", fullPath);
            config = JsonConvert.DeserializeObject<CsCodeGeneratorConfig>(System.IO.File.ReadAllText(fullPath))
                ?? throw new InvalidOperationException($"Unable to deserialize configuration '{fullPath}'.");
            string directory = System.IO.Path.GetDirectoryName(fullPath)!;
            this.m_composer.Compose(ref config, directory);
            config.configDirectory = directory;
            this.m_presets.Apply(config);
            config.presetDefaultsApplied = true;
        }

        ConfigValidator.Validate(config);
        config.LoadConfiguredPlugins();
        return config;
    }
}
