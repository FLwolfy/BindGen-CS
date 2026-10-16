using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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
