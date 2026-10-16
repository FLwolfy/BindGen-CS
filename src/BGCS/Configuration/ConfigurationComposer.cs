using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Infrastructure.IO;

namespace BGCS.Configuration;

internal sealed class ConfigurationComposer(HttpClient? httpClient = null)
{
    private const string C_VALUE_ORIGINS = "__bgcsValuePathBases";
    private readonly JsonSerializerOptions m_options = BindingConfigurationLoader.CreateSerializerOptions();
    private readonly HashSet<string> m_active = new(StringComparer.Ordinal);
    private readonly Dictionary<string, InputDependency> m_inputs = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly Dictionary<string, PinnedConfigurationInput> m_remoteInputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (ContentDigest Digest, byte[] Content)> m_remoteContents = new(StringComparer.Ordinal);
    private readonly RemoteConfigurationReader m_remoteReader = new(httpClient);
    private int m_documents;
    private long m_totalBytes;

    internal async Task<ConfigurationLoadResult> LoadAsync(
        string path,
        string? selectedProfile,
        CancellationToken cancellationToken
    ) {
        string absolute = Path.GetFullPath(path);
        JsonObject combined = await ReadAsync(ConfigurationReference.Local(absolute), Path.GetDirectoryName(absolute)!, 0, cancellationToken).ConfigureAwait(false);
        string? profile = selectedProfile ?? combined["profile"]?.GetValue<string>();
        if (profile is not null)
        {
            JsonObject presets = combined["profiles"] as JsonObject ?? throw new ArgumentException("The configuration declares no named profiles.");
            JsonObject preset = presets[profile] as JsonObject ?? throw new ArgumentException($"Named profile '{profile}' is unavailable.");
            combined = Merge((JsonObject)preset.DeepClone(), combined);
        }
        combined.Remove("extends");
        combined.Remove("profiles");
        combined.Remove("profile");
        Dictionary<string, ImmutableDictionary<string, string>> origins = new(StringComparer.Ordinal);
        if (combined["extensions"]?["options"] is JsonObject namespaces)
            foreach ((string id, JsonNode? node) in namespaces)
                if (node is JsonObject settings)
                {
                    JsonObject captured = settings[C_VALUE_ORIGINS] as JsonObject ?? new();
                    string fallback = settings["pathBase"]!.GetValue<string>();
                    origins.Add(id, Leaves(settings["values"] ?? new JsonObject(), string.Empty).ToImmutableDictionary(pointer => pointer,
                        pointer => captured[pointer]?.GetValue<string>() ?? fallback, StringComparer.Ordinal));
                    settings.Remove(C_VALUE_ORIGINS);
                }
        BindingConfiguration value = combined.Deserialize<BindingConfiguration>(m_options) ?? throw new JsonException("A binding configuration cannot be null.");
        value = value with
        {
            extensions = value.extensions with
            {
                options = value.extensions.options.ToImmutableDictionary(pair => pair.Key,
            pair => pair.Value with { valuePathBases = origins[pair.Key] }, StringComparer.Ordinal)
            }
        };
        return new(new BindingConfigurationLoader().ValidateAndFreeze(value), m_inputs.Values.OrderBy(input => input.path, StringComparer.Ordinal).ToImmutableArray())
        { remoteInputs = m_remoteInputs.Values.OrderBy(input => input.url.AbsoluteUri, StringComparer.Ordinal).ThenBy(input => input.pathBase, StringComparer.Ordinal).ToImmutableArray() };
    }

    private async Task<JsonObject> ReadAsync(
        ConfigurationReference reference,
        string declaringDirectory,
        int depth,
        CancellationToken cancellationToken
    ) {
        cancellationToken.ThrowIfCancellationRequested();
        if (depth >= 64) throw new JsonException("Configuration inheritance exceeds 64 documents in one dependency path.");
        if (++m_documents > 128) throw new JsonException("Configuration composition exceeds the 128-document limit.");
        string? path = reference.path is string local ? Path.GetFullPath(local, declaringDirectory) : null;
        string directory = path is not null ? Path.GetDirectoryName(path)! : Path.GetFullPath(reference.pathBase!, declaringDirectory);
        string identity = path is not null ? "file:" + (OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path)
            : "https:" + reference.url!.AbsoluteUri + "|" + directory;
        if (!m_active.Add(identity)) throw new ArgumentException("Configuration inheritance contains a cycle at '" + (path ?? reference.url!.GetLeftPart(UriPartial.Path)) + "'.");
        try
        {
            byte[] content;
            if (path is not null)
            {
                await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
                content = await RemoteConfigurationReader.ReadBoundedAsync(stream, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                Uri remoteUrl = reference.url ?? throw new JsonException("A remote base reference requires an HTTPS URL.");
                string url = remoteUrl.AbsoluteUri;
                if (m_remoteContents.TryGetValue(url, out var previousRemote))
                {
                    if (previousRemote.Digest != reference.digest!.Value) throw new IOException("One remote configuration URL has conflicting content pins.");
                    content = previousRemote.Content;
                }
                else
                {
                    content = await m_remoteReader.ReadAsync(remoteUrl, reference.digest!.Value, cancellationToken).ConfigureAwait(false);
                    m_remoteContents.Add(url, (reference.digest.Value, content));
                }
                m_remoteInputs[identity] = new(remoteUrl, reference.digest!.Value, content.Length, directory);
            }
            m_totalBytes = checked(m_totalBytes + content.Length);
            if (m_totalBytes > 64 * 1024 * 1024) throw new IOException("Composed configuration inputs exceed the 64 MiB byte budget.");
            ContentDigest digest = new(Convert.ToHexStringLower(SHA256.HashData(content)));
            if (path is not null)
            {
                if (m_inputs.TryGetValue(path, out InputDependency? previous) && previous.digest != digest) throw new IOException("A base configuration changed while composing inputs.");
                m_inputs[path] = new(path, PhysicalPathResolver.Resolve(path), content.Length, digest);
            }
            JsonObject current = JsonNode.Parse(content, documentOptions: new JsonDocumentOptions { MaxDepth = 64 }) as JsonObject ?? throw new JsonException("A binding document must be an object.");
            _ = current.Deserialize<BindingConfiguration>(m_options) ?? throw new JsonException("A binding document cannot be null.");
            if (current["schemaVersion"]?.GetValue<int>() != 1) throw new JsonException("Every configuration document must declare schemaVersion 1.");
            ValidateFragment(current);
            cancellationToken.ThrowIfCancellationRequested();
            JsonObject merged = new();
            if (current["extends"] is JsonArray bases)
                foreach (JsonNode? node in bases)
                {
                    ConfigurationReference next = node?.Deserialize<ConfigurationReference>(m_options) ?? throw new JsonException("A base configuration reference cannot be null.");
                    merged = Merge(merged, await ReadAsync(next, directory, depth + 1, cancellationToken).ConfigureAwait(false));
                }
            NormalizePaths(current, directory);
            if (current["profiles"] is JsonObject profiles)
                foreach ((string id, JsonNode? node) in profiles)
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace(id);
                    if (node is not JsonObject preset) throw new JsonException("A named profile must be an object.");
                    if (preset.ContainsKey("extends") || preset.ContainsKey("profiles") || preset.ContainsKey("profile") || preset.ContainsKey("schemaVersion") || preset.ContainsKey("$schema"))
                        throw new JsonException("A named profile is a settings fragment and cannot change document identity or inheritance.");
                    JsonObject probe = (JsonObject)preset.DeepClone();
                    probe["schemaVersion"] = 1;
                    _ = probe.Deserialize<BindingConfiguration>(m_options);
                    ValidateFragment(probe);
                    NormalizePaths(preset, directory);
                }
            return Merge(merged, current);
        }
        finally { m_active.Remove(identity); }
    }

    private void ValidateFragment(JsonObject fragment)
    {
        if (fragment["extensions"]?["options"] is JsonObject namespaces)
            foreach ((string _, JsonNode? node) in namespaces)
                if (node is JsonObject settings && settings.Any(pair => pair.Key is not ("values" or "pathBase")))
                    throw new JsonException("Plugin settings wrappers accept only values and pathBase; origins are computed from actual inputs.");
        JsonObject probe = (JsonObject)fragment.DeepClone();
        probe.Remove("extends");
        probe.Remove("profiles");
        probe.Remove("profile");
        probe["schemaVersion"] = 1;
        JsonObject library = probe["library"] as JsonObject ?? new();
        if (!library.ContainsKey("id")) library["id"] = "fragment";
        if (!library.ContainsKey("nativeName")) library["nativeName"] = "fragment";
        if (probe["library"] is null) probe["library"] = library;
        JsonObject source = probe["source"] as JsonObject ?? new();
        if (!source.ContainsKey("headers")) source["headers"] = new JsonArray("__bgcs_fragment_validation__.h");
        if (probe["source"] is null) probe["source"] = source;
        BindingConfiguration typed = probe.Deserialize<BindingConfiguration>(m_options) ?? throw new JsonException("A configuration fragment cannot be null.");
        _ = new BindingConfigurationLoader().ValidateAndFreeze(typed);
    }

    private static JsonObject Merge(
        JsonObject lower,
        JsonObject higher
    ) {
        foreach ((string name, JsonNode? value) in higher)
            if (value is JsonObject overlay && lower[name] is JsonObject baseline) Merge(baseline, overlay);
            else lower[name] = value?.DeepClone();
        return lower;
    }

    private static void NormalizePaths(
        JsonObject root,
        string directory
    ) {
        foreach (string property in new[] { "includeHeaders", "excludeHeaders" })
            if (root["source"]?["selection"]?[property] is JsonArray headers) NormalizeArray(headers);
        foreach ((string group, string property) in new[] { ("source", "headers"), ("source", "includeDirectories"), ("source", "systemIncludeDirectories"),
            ("nativeBuild", "sources"), ("nativeBuild", "artifacts"), ("nativeBuild", "libraryDirectories"), ("managedBuild", "references"), ("managedBuild", "sources"), ("extensions", "plugins") })
            if (root[group]?[property] is JsonArray paths) NormalizeArray(paths);
        foreach ((string group, string property) in new[] { ("output", "root"), ("cache", "directory") })
            if (root[group]?[property] is JsonValue value) root[group]![property] = Path.GetFullPath(value.GetValue<string>(), directory);
        if (root["nativeBuild"]?["libraries"] is JsonArray libraries)
            for (int index = 0; index < libraries.Count; index++)
            {
                string library = libraries[index]?.GetValue<string>() ?? throw new JsonException("A native library cannot be null.");
                if (library.Contains('/') || library.Contains('\\') || Path.HasExtension(library)) libraries[index] = Path.GetFullPath(library, directory);
            }
        foreach (string property in new[] { "buildToolPath", "auxiliaryToolPath" })
            if (root["nativeBuild"]?[property] is JsonValue executable)
            {
                string tool = executable.GetValue<string>();
                if (tool.Contains('/') || tool.Contains('\\') || Path.IsPathRooted(tool)) root["nativeBuild"]![property] = Path.GetFullPath(tool, directory);
            }
        if (root["cpp"]?["nativeShims"] is JsonArray shims)
            foreach (JsonNode? shim in shims)
                foreach (string property in new[] { "publicHeaders", "sourceFiles" })
                    if (shim?[property] is JsonArray paths) NormalizeArray(paths);
        foreach (string group in new[] { "typeLowerings", "callableLowerings" })
            if (root["cpp"]?[group] is JsonArray recipes)
                foreach (JsonNode? recipe in recipes)
                    if (recipe?["sourceFragments"] is JsonArray fragments)
                        foreach (JsonNode? fragment in fragments)
                            if (fragment?["requiredHeaders"] is JsonArray paths) NormalizeArray(paths);
        if (root["targets"] is JsonArray targets)
            foreach (JsonNode? target in targets)
                if (target?["settings"] is JsonObject settings)
                {
                    foreach (string property in new[] { "systemIncludeDirectories", "cxxSystemIncludeDirectories" }) if (settings[property] is JsonArray paths) NormalizeArray(paths);
                    if (settings["sysRoot"] is JsonValue sysroot) settings["sysRoot"] = Path.GetFullPath(sysroot.GetValue<string>(), directory);
                    if (settings["compilerPath"] is JsonValue executable)
                    {
                        string compiler = executable.GetValue<string>();
                        if (compiler.Contains('/') || compiler.Contains('\\') || Path.IsPathRooted(compiler)) settings["compilerPath"] = Path.GetFullPath(compiler, directory);
                    }
                }
        if (root["extensions"]?["options"] is JsonObject namespaces)
            foreach ((string _, JsonNode? value) in namespaces)
                if (value is JsonObject settings)
                {
                    settings["pathBase"] = settings["pathBase"] is JsonValue pathBase ? Path.GetFullPath(pathBase.GetValue<string>(), directory) : directory;
                    string origin = settings["pathBase"]!.GetValue<string>();
                    JsonObject origins = new();
                    if (settings["values"] is JsonNode values)
                        foreach (string pointer in Leaves(values, string.Empty)) origins[pointer] = origin;
                    settings[C_VALUE_ORIGINS] = origins;
                }

        void NormalizeArray(JsonArray paths)
        {
            for (int index = 0; index < paths.Count; index++) paths[index] = Path.GetFullPath(paths[index]?.GetValue<string>() ?? throw new JsonException("A configured path cannot be null."), directory);
        }
    }

    private static IEnumerable<string> Leaves(
        JsonNode? node,
        string pointer
    ) {
        if (node is JsonObject value && value.Count > 0)
        {
            foreach ((string name, JsonNode? child) in value)
                foreach (string leaf in Leaves(child, pointer + "/" + name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal)))
                    yield return leaf;
        }
        else yield return pointer;
    }
}
