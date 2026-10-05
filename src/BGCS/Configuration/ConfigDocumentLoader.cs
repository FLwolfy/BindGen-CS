using System;
using System.IO;

namespace BGCS.Configuration;

using BGCS.Core.Collections;
using BGCS.Core.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Loads JSON configuration documents while preserving which values were explicitly specified.
/// </summary>
/// <remarks>
/// Presets are applied to a fresh default configuration before base and child documents are merged.
/// This makes presets true defaults: any explicit JSON value, including a value equal to the normal
/// BindGen-CS default, wins over the preset.
/// </remarks>
internal sealed class ConfigDocumentLoader
{
    private static readonly JsonMergeSettings MergeSettings = new()
    {
        MergeArrayHandling = MergeArrayHandling.Union,
        MergeNullValueHandling = MergeNullValueHandling.Merge,
        PropertyNameComparison = StringComparison.Ordinal
    };
    private readonly PresetResolver m_presets;
    public ConfigDocumentLoader(PresetResolver presets)
    {
        this.m_presets = presets ?? throw new ArgumentNullException(nameof(presets));
    }

    public CsCodeGeneratorConfig Load(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Configuration file not found: {fullPath}", fullPath);
        }

        JObject document = JsonConfigurationComposer.Compose(
            JObject.Parse(File.ReadAllText(fullPath)),
            Path.GetDirectoryName(fullPath)!,
            fullPath);
        CsCodeGeneratorConfig baseline = CsCodeGeneratorConfig.@default;
        baseline.preset = document.Value<string>(nameof(CsCodeGeneratorConfig.preset)) ?? string.Empty;
        this.m_presets.Apply(baseline);
        JsonSerializer serializer = CsCodeGeneratorConfig.CreateMergeSerializer();
        JObject merged = JObject.FromObject(baseline, serializer);
        merged.Merge(document, MergeSettings);
        CsCodeGeneratorConfig config = merged.ToObject<CsCodeGeneratorConfig>(serializer) ?? throw new InvalidOperationException($"Unable to deserialize BindGen-CS configuration '{fullPath}'.");
        CollectionNormalizer.Normalize(config);
        config.configDirectory = Path.GetDirectoryName(fullPath);
        config.presetDefaultsApplied = true;
        return config;
    }

}
