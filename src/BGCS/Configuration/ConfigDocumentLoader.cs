namespace BGCS.Configuration;

using BGCS.Core.Collections;
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
    private const string FileProtocol = "file://";
    private const string HttpProtocol = "http://";
    private const string HttpsProtocol = "https://";

    private static readonly JsonMergeSettings MergeSettings = new()
    {
        MergeArrayHandling = MergeArrayHandling.Union,
        MergeNullValueHandling = MergeNullValueHandling.Merge,
        PropertyNameComparison = StringComparison.Ordinal
    };

    private readonly PresetResolver presets;

    public ConfigDocumentLoader(PresetResolver presets)
    {
        this.presets = presets ?? throw new ArgumentNullException(nameof(presets));
    }

    public CsCodeGeneratorConfig Load(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            CsCodeGeneratorConfig.Default.Save(fullPath);
        }

        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase) { fullPath };
        JObject document = LoadDocument(File.ReadAllText(fullPath), Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory, visited);

        CsCodeGeneratorConfig baseline = CsCodeGeneratorConfig.Default;
        baseline.Preset = document.Value<string>(nameof(CsCodeGeneratorConfig.Preset)) ?? string.Empty;
        presets.Apply(baseline);

        JObject merged = JObject.FromObject(baseline, CsCodeGeneratorConfig.MergeSerializer);
        merged.Merge(document, MergeSettings);

        CsCodeGeneratorConfig config = merged.ToObject<CsCodeGeneratorConfig>(CsCodeGeneratorConfig.MergeSerializer)
            ?? throw new InvalidOperationException($"Unable to deserialize BindGen-CS configuration '{fullPath}'.");
        CollectionNormalizer.Normalize(config);
        config.ConfigDirectory = Path.GetDirectoryName(fullPath);
        config.PresetDefaultsApplied = true;
        return config;
    }

    private JObject LoadDocument(string json, string baseDirectory, HashSet<string> visited)
    {
        JObject current = JObject.Parse(json);
        if (current[nameof(CsCodeGeneratorConfig.BaseConfig)] is not JObject baseReference ||
            string.IsNullOrWhiteSpace(baseReference.Value<string>(nameof(BaseConfig.Url))))
        {
            return current;
        }

        string url = baseReference.Value<string>(nameof(BaseConfig.Url))!;
        (string source, string contents, string nextBaseDirectory) = ReadSource(url, baseDirectory);
        if (!visited.Add(source))
        {
            throw new InvalidOperationException($"Circular BaseConfig reference detected: {source}");
        }

        JObject baseDocument = LoadDocument(contents, nextBaseDirectory, visited);
        baseDocument.Remove(nameof(CsCodeGeneratorConfig.BaseConfig));
        ApplyConstraints(baseDocument, baseReference[nameof(BaseConfig.IgnoredProperties)] as JArray);
        baseDocument.Merge(current, MergeSettings);
        return baseDocument;
    }

    private static (string Source, string Contents, string BaseDirectory) ReadSource(string url, string baseDirectory)
    {
        if (url.StartsWith(FileProtocol, StringComparison.OrdinalIgnoreCase))
        {
            string path = Path.GetFullPath(url[FileProtocol.Length..], baseDirectory);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Base configuration file not found: {path}", path);
            }

            return (path, File.ReadAllText(path), Path.GetDirectoryName(path) ?? baseDirectory);
        }

        if (url.StartsWith(HttpProtocol, StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith(HttpsProtocol, StringComparison.OrdinalIgnoreCase))
        {
            using HttpClient client = new();
            using HttpResponseMessage response = client.GetAsync(url).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Unable to load base configuration '{url}': HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
            }

            string contents = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return (url, contents, baseDirectory);
        }

        throw new InvalidOperationException($"Invalid BaseConfig URL '{url}'. Use file://, http://, or https://.");
    }

    private static void ApplyConstraints(JObject document, JArray? ignoredProperties)
    {
        if (ignoredProperties == null)
            return;

        foreach (JToken item in ignoredProperties)
        {
            string? path = item.Value<string>()?.Trim();
            if (string.IsNullOrEmpty(path))
                continue;

            JToken? token = document.SelectToken(path, errorWhenNoMatch: false);
            if (token is JArray array)
            {
                array.RemoveAll();
            }
            else if (token?.Parent is JProperty property)
            {
                property.Remove();
            }
            else
            {
                token?.Remove();
            }
        }
    }
}
