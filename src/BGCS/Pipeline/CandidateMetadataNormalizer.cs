using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BGCS;

/// <summary>Normalizes private candidate paths in generated metadata while retaining unchanged JSON values.</summary>
internal sealed class CandidateMetadataNormalizer
{
    private readonly ImmutableArray<string> m_candidateOrigins;
    private readonly string m_logicalOutput;
    private readonly string? m_previewOutput;
    private readonly StringComparison m_comparison;
    private readonly StringComparer m_exclusionComparer;

    internal CandidateMetadataNormalizer(
        IEnumerable<string> normalizedCandidateOrigins,
        string logicalOutput,
        string? normalizedPreviewOutput,
        bool ignoreCase
    ) {
        m_candidateOrigins = normalizedCandidateOrigins.ToImmutableArray();
        m_logicalOutput = logicalOutput;
        m_previewOutput = normalizedPreviewOutput;
        m_comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        m_exclusionComparer = ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    }

    internal void Normalize(JsonNode? node)
    {
        if (node is JsonObject properties)
        {
            List<(string name, JsonNode? value)>? replacements = null;
            // Recurse into child values without mutating this parent's property
            // collection. Most metadata strings require no new node or key array.
            foreach (var property in properties)
            {
                if (property.Value is JsonValue scalar && scalar.TryGetValue(out string? text))
                {
                    string? mapped = Logical(text);
                    if (!string.Equals(mapped, text, StringComparison.Ordinal))
                        (replacements ??= []).Add((property.Key, JsonValue.Create(mapped)));
                }
                else Normalize(property.Value);
                if (property.Key == "excludedDirectories" && property.Value is JsonArray excluded)
                {
                    JsonArray normalized = new(excluded.Select(value => value!.GetValue<string>()).Distinct(m_exclusionComparer)
                        .Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
                    (replacements ??= []).Add((property.Key, normalized));
                }
            }
            if (replacements is not null)
                foreach (var replacement in replacements) properties[replacement.name] = replacement.value;
        }
        else if (node is JsonArray values)
            for (int index = 0; index < values.Count; index++)
            {
                if (values[index] is JsonValue scalar && scalar.TryGetValue(out string? text))
                {
                    string? mapped = Logical(text);
                    if (!string.Equals(mapped, text, StringComparison.Ordinal)) values[index] = mapped;
                }
                else Normalize(values[index]);
            }
    }

    internal void WriteNormalized(
        JsonElement document,
        Utf8JsonWriter writer
    ) {
        ArgumentNullException.ThrowIfNull(writer);
        List<PropertyScope> scopes = [];
        HashSet<string> exclusions = new(m_exclusionComparer);
        try { Write(document, mapString: false, depth: 0); }
        catch (Exception failure) when (failure is ArgumentException or InvalidOperationException or JsonException or NullReferenceException)
        {
            // Normalization originally completed before writing started. On a
            // failed stream, preserve any earlier normalization failure instead
            // of letting a writer depth error hide malformed later exclusions.
            Normalize(JsonNode.Parse(document.GetRawText(), documentOptions: new() { MaxDepth = int.MaxValue }));
            throw;
        }

        void Write(
            JsonElement value,
            bool mapString,
            int depth
        ) {
            if (value.ValueKind == JsonValueKind.Object)
            {
                while (scopes.Count <= depth) scopes.Add(new());
                PropertyScope scope = scopes[depth];
                scope.properties.Clear();
                scope.names.Clear();
                foreach (JsonProperty property in value.EnumerateObject())
                {
                    string name = property.Name;
                    if (!scope.names.Add(name))
                    {
                        // JsonObject materializes all names before visiting its
                        // children. Retain its duplicate-name failure semantics.
                        JsonNode? duplicate = JsonNode.Parse(value.GetRawText());
                        Normalize(duplicate);
                        duplicate!.WriteTo(writer);
                        return;
                    }
                    scope.properties.Add((name, property.Value));
                }
                writer.WriteStartObject();
                foreach (var property in scope.properties)
                {
                    writer.WritePropertyName(property.name);
                    if (property.name == "excludedDirectories" && property.value.ValueKind == JsonValueKind.Array)
                        WriteExclusions(property.value);
                    else Write(property.value, mapString: true, depth + 1);
                }
                writer.WriteEndObject();
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                writer.WriteStartArray();
                foreach (JsonElement item in value.EnumerateArray()) Write(item, mapString: true, depth + 1);
                writer.WriteEndArray();
            }
            else if (mapString && value.ValueKind == JsonValueKind.String)
            {
                string? text = value.GetString();
                string? mapped = Logical(text);
                if (string.Equals(mapped, text, StringComparison.Ordinal)) value.WriteTo(writer);
                else writer.WriteStringValue(mapped);
            }
            else value.WriteTo(writer);
        }

        void WriteExclusions(JsonElement values)
        {
            foreach (JsonElement item in values.EnumerateArray())
                if (item.ValueKind != JsonValueKind.String)
                {
                    // Invalid arrays still visit all children before the old
                    // string extraction fails, including nested exclusions.
                    JsonArray malformed = JsonNode.Parse(values.GetRawText())!.AsArray();
                    Normalize(malformed);
                    string[] normalized = malformed.Select(value => value!.GetValue<string>()).Distinct(m_exclusionComparer).ToArray();
                    writer.WriteStartArray();
                    foreach (string text in normalized) writer.WriteStringValue(text);
                    writer.WriteEndArray();
                    return;
                }
            exclusions.Clear();
            writer.WriteStartArray();
            foreach (JsonElement item in values.EnumerateArray())
            {
                string text = Logical(item.GetString())!;
                if (exclusions.Add(text)) writer.WriteStringValue(text);
            }
            writer.WriteEndArray();
        }
    }

    private sealed class PropertyScope
    {
        internal List<(string name, JsonElement value)> properties { get; } = [];
        internal HashSet<string> names { get; } = new(StringComparer.Ordinal);
    }

    private string? Logical(string? text)
    {
        if (text is null) return null;
        string candidate = text.Replace('\\', '/');
        if (m_previewOutput is not null && candidate.TrimEnd('/').Equals(m_previewOutput, m_comparison))
            return m_logicalOutput;
        foreach (string normalized in m_candidateOrigins)
        {
            if (candidate.Equals(normalized, m_comparison)) return "artifact:/";
            if (candidate.StartsWith(normalized + "/", m_comparison)) return "artifact:/" + candidate[(normalized.Length + 1)..];
        }
        return text;
    }
}
