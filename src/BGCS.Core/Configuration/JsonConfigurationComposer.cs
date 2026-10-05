using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using Newtonsoft.Json.Linq;

namespace BGCS.Core.Configuration;

/// <summary>
/// Resolves JSON base configuration references without discarding explicitly supplied scalar values.
/// </summary>
public static class JsonConfigurationComposer
{
    private static readonly JsonMergeSettings MergeSettings = new()
    {
        MergeArrayHandling = MergeArrayHandling.Union,
        MergeNullValueHandling = MergeNullValueHandling.Merge,
        PropertyNameComparison = StringComparison.Ordinal
    };

    /// <summary>
    /// Composes a document and its base chain into a fresh document with inheritance references removed.
    /// </summary>
    /// <param name="document">
    /// Child document. A baseConfig object may specify url and ignoredProperties; the input is not modified.
    /// </param>
    /// <param name="baseDirectory">
    /// Initial directory for file:// references; each loaded file supplies the origin for its own references.
    /// </param>
    /// <param name="sourcePath">
    /// Optional path of the initial document, used to detect a reference back to that document.
    /// </param>
    /// <returns>
    /// A composed JSON object. Child scalars override base scalars, arrays are unioned and ignored base values are removed.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// The initial directory is empty or contains only whitespace.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// The child document is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The inheritance chain is circular or a reference uses an unsupported protocol.
    /// </exception>
    /// <exception cref="FileNotFoundException">
    /// A referenced configuration file does not exist.
    /// </exception>
    /// <exception cref="HttpRequestException">
    /// A remote configuration cannot be retrieved successfully.
    /// </exception>
    public static JObject Compose(
        JObject document,
        string baseDirectory,
        string? sourcePath = null
    ) {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        string currentDirectory = Path.GetFullPath(baseDirectory);
        HashSet<string> visitedFiles = new(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        HashSet<string> visitedUrls = new(StringComparer.Ordinal);
        List<string> sourceChain = [];
        if (sourcePath != null)
        {
            string initialPath = Path.GetFullPath(sourcePath, currentDirectory);
            visitedFiles.Add(initialPath);
            sourceChain.Add(initialPath);
        }

        Stack<JObject> documents = new();
        JObject current = (JObject)document.DeepClone();
        while (true)
        {
            documents.Push(current);
            if (current["baseConfig"] is not JObject reference
                || string.IsNullOrWhiteSpace(reference.Value<string>("url")))
                break;

            string url = reference.Value<string>("url")!;
            if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                string path = Path.GetFullPath(url["file://".Length..], currentDirectory);
                sourceChain.Add(path);
                if (!visitedFiles.Add(path))
                    throw new InvalidOperationException($"Circular BaseConfig reference detected: {string.Join(" -> ", sourceChain)}");
                if (!File.Exists(path))
                    throw new FileNotFoundException($"Base configuration file not found: {path}", path);
                current = JObject.Parse(File.ReadAllText(path));
                currentDirectory = Path.GetDirectoryName(path)!;
            }
            else if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
                && uri.Scheme is "http" or "https")
            {
                sourceChain.Add(uri.AbsoluteUri);
                if (!visitedUrls.Add(uri.AbsoluteUri))
                    throw new InvalidOperationException($"Circular BaseConfig reference detected: {string.Join(" -> ", sourceChain)}");
                using HttpClient client = new();
                using HttpResponseMessage response = client.GetAsync(uri).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();
                current = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            }
            else
            {
                throw new InvalidOperationException($"Invalid BaseConfig URL '{url}'. Use file://, http://, or https://.");
            }
        }

        JObject composed = new();
        while (documents.TryPop(out JObject? child))
        {
            ApplyConstraints(composed, (child["baseConfig"] as JObject)?["ignoredProperties"] as JArray);
            child.Remove("baseConfig");
            composed.Merge(child, MergeSettings);
        }
        return composed;
    }

    private static void ApplyConstraints(
        JObject document,
        JArray? ignoredProperties
    ) {
        if (ignoredProperties == null)
            return;
        foreach (JToken item in ignoredProperties)
        {
            string? path = item.Value<string>()?.Trim();
            if (string.IsNullOrEmpty(path))
                continue;
            JToken? token = document.SelectToken(path, errorWhenNoMatch: false);
            if (token?.Parent is JProperty property)
                property.Remove();
            else
                token?.Remove();
        }
    }
}
