namespace BGCS.Configuration;

/// <summary>
/// Loads composed generator configuration without rewriting an existing source document.
/// </summary>
public sealed class ConfigLoader
{
    private readonly IConfigComposer? composer;
    private readonly PresetResolver presets;

    public ConfigLoader(IConfigComposer? composer = null, PresetResolver? presets = null)
    {
        this.composer = composer;
        this.presets = presets ?? PresetResolver.Default;
    }

    public CsCodeGeneratorConfig Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A configuration path is required.", nameof(path));
        CsCodeGeneratorConfig config;
        if (composer == null || composer is ConfigComposer)
        {
            config = new ConfigDocumentLoader(presets).Load(path);
        }
        else
        {
            config = CsCodeGeneratorConfig.Load(path, composer);
            presets.Apply(config);
            config.PresetDefaultsApplied = true;
        }
        ConfigValidator.Validate(config);
        return config;
    }
}
