using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace BGCS.Infrastructure.Configuration;

/// <summary>Validates bundled plugin settings schemas without global mutation or implicit external schema fetching.</summary>
public sealed class ExtensionSchemaValidator
{
    /// <summary>Checks one settings object using a self-contained Draft 2020-12 schema.</summary>
    /// <param name="schema">The plugin-owned bundled schema.</param>
    /// <param name="settings">The complete plugin settings namespace.</param>
    /// <param name="pluginId">The owning plugin identity used in diagnostics.</param>
    public void Validate(
        JsonElement schema,
        JsonElement settings,
        string pluginId
    ) {
        if (settings.ValueKind != JsonValueKind.Object) throw new ArgumentException("Plugin settings must be an object.");
        if (Encoding.UTF8.GetByteCount(settings.GetRawText()) > 1024 * 1024) throw new ArgumentException("A plugin settings object exceeds the 1 MiB input limit.");
        EvaluationResults result = Build(schema).Evaluate(settings, new EvaluationOptions { OutputFormat = OutputFormat.Hierarchical, Culture = CultureInfo.InvariantCulture, RequireFormatValidation = true });
        if (result.IsValid) return;
        List<string> errors = [];
        Stack<EvaluationResults> pending = new();
        pending.Push(result);
        while (pending.Count != 0 && errors.Count < 32)
        {
            EvaluationResults current = pending.Pop();
            if (current.Errors is not null) errors.AddRange(current.Errors.OrderBy(error => error.Key, StringComparer.Ordinal).Select(error => current.InstanceLocation + ": " + error.Value));
            if (current.Details is not null) foreach (EvaluationResults child in current.Details.AsEnumerable().Reverse()) if (!child.IsValid) pending.Push(child);
        }
        string detail = string.Join(Environment.NewLine, errors.Take(32));
        if (detail.Length > 16 * 1024) detail = detail[..(16 * 1024)];
        throw new ArgumentException($"Plugin '{pluginId}' settings failed its declared schema:{Environment.NewLine}{detail}");
    }

    /// <summary>Checks schema validity and the complete local reference graph during metadata discovery.</summary>
    /// <param name="schema">The immutable plugin schema.</param>
    public void ValidateSchema(JsonElement schema) => _ = Build(schema);

    private static JsonSchema Build(JsonElement schema)
    {
        if (schema.ValueKind is not (JsonValueKind.Object or JsonValueKind.True or JsonValueKind.False)) throw new ArgumentException("An extension schema must be an object or boolean.");
        string text = schema.GetRawText();
        if (Encoding.UTF8.GetByteCount(text) > 256 * 1024) throw new ArgumentException("An extension schema exceeds the 256 KiB metadata limit.");
        JsonNode? node = JsonNode.Parse(text, documentOptions: new() { MaxDepth = 64 });
        ValidateReferences(node, root: true);
        string digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        if (node is JsonObject root) root["$id"] = "urn:bgcs:extension-schema:" + digest;
        using JsonDocument document = JsonDocument.Parse(node!.ToJsonString(), new() { MaxDepth = 64 });
        SchemaRegistry registry = new()
        {
            Fetch = (
                uri,
                _
            ) => throw new NotSupportedException($"External extension schema '{uri}' requires an explicit locked schema input.")
        };
        return JsonSchema.Build(document.RootElement.Clone(), new BuildOptions { Dialect = Dialect.Draft202012, SchemaRegistry = registry }, new Uri("urn:bgcs:extension-schema:" + digest));
    }

    private static void ValidateReferences(
        JsonNode? node,
        bool root
    ) {
        if (node is not JsonObject schema) return;
        if (schema["$schema"] is JsonValue dialect && dialect.GetValue<string>().TrimEnd('#') != "https://json-schema.org/draft/2020-12/schema")
            throw new NotSupportedException("Plugin settings schemas use the fixed Draft 2020-12 dialect.");
        if (root && schema["$id"] is JsonValue identity && !Uri.TryCreate(identity.GetValue<string>(), UriKind.RelativeOrAbsolute, out _))
            throw new ArgumentException("An extension schema resource identity must be a valid URI reference.");
        if (!root && schema.ContainsKey("$id")) throw new NotSupportedException("Nested extension schema resources must be bundled through $defs and local anchors.");
        foreach (string keyword in new[] { "$ref", "$dynamicRef" })
            if (schema[keyword] is JsonValue reference && reference.GetValue<string>() is string value && value.Length != 0 && !value.StartsWith('#'))
                throw new NotSupportedException("Extension schema references must resolve inside the bundled document.");
        foreach (string keyword in new[] { "$defs", "properties", "patternProperties", "dependentSchemas" })
            if (schema[keyword] is JsonObject children) foreach (JsonNode? child in children.Select(pair => pair.Value)) ValidateReferences(child, root: false);
        foreach (string keyword in new[] { "allOf", "anyOf", "oneOf", "prefixItems" })
            if (schema[keyword] is JsonArray children) foreach (JsonNode? child in children) ValidateReferences(child, root: false);
        foreach (string keyword in new[] { "additionalProperties", "unevaluatedProperties", "items", "unevaluatedItems", "contains", "not", "if", "then", "else", "propertyNames", "contentSchema" })
            if (schema[keyword] is JsonNode child) ValidateReferences(child, root: false);
    }
}
