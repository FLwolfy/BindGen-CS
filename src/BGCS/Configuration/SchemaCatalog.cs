using System;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using BGCS.Contracts.Plugins;

namespace BGCS.Configuration;

/// <summary>Exports the new grouped configuration schema from the actual serializer metadata and declared defaults.</summary>
public sealed class SchemaCatalog
{
    /// <summary>Exports the strict workspace graph schema from the actual input contract.</summary>
    /// <returns>The independent workspace schema JSON document.</returns>
    public JsonObject CreateWorkspaceSchema()
    {
        JsonSerializerOptions options = BindingConfigurationLoader.CreateSerializerOptions();
        options.MakeReadOnly(populateMissingResolver: true);
        JsonObject root = (JsonObject)options.GetJsonSchemaAsNode(typeof(WorkspaceConfiguration), new JsonSchemaExporterOptions
        { TreatNullObliviousAsNonNullable = true });
        root["$schema"] = "https://json-schema.org/draft/2020-12/schema";
        root["title"] = "BGCS 3.0 workspace configuration";
        root["properties"]!["schemaVersion"]!["const"] = 1;
        root["properties"]!["parallelism"]!["minimum"] = 1;
        root["properties"]!["parallelism"]!["maximum"] = 1024;
        root["properties"]!["parallelism"]!["default"] = 2;
        root["properties"]!["projects"]!["minItems"] = 1;
        JsonObject project = (JsonObject)root["properties"]!["projects"]!["items"]!;
        project["required"] = new JsonArray("id", "configuration");
        project["properties"]!["id"]!["minLength"] = 1;
        project["properties"]!["configuration"]!["minLength"] = 1;
        foreach (string list in new[] { "dependencies", "targets" })
        {
            project["properties"]![list]!["uniqueItems"] = true;
            project["properties"]![list]!["items"]!["minLength"] = 1;
        }
        root["required"] = new JsonArray("schemaVersion", "projects");
        return root;
    }

    /// <summary>Exports the exact strict plugin manifest contract consumed by the isolated loader.</summary>
    /// <returns>The independent manifest schema JSON document.</returns>
    public JsonObject CreatePluginSchema()
    {
        JsonSerializerOptions options = new()
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
        };
        options.MakeReadOnly(populateMissingResolver: true);
        JsonObject root = (JsonObject)options.GetJsonSchemaAsNode(typeof(PluginManifest), new JsonSchemaExporterOptions
        { TreatNullObliviousAsNonNullable = true });
        root["$schema"] = "https://json-schema.org/draft/2020-12/schema";
        root["title"] = "BGCS 3.0 neutral plugin manifest";
        root["properties"]!["schemaVersion"]!["const"] = 1;
        root["properties"]!["assets"]!["minItems"] = 1;
        root["properties"]!["plugin"]!["properties"]!["contractsMajor"]!["const"] = BGCS.Contracts.Versioning.ContractVersion.C_MAJOR;
        root["properties"]!["plugin"]!["properties"]!["minimumApiMinor"]!["minimum"] = 0;
        root["properties"]!["plugin"]!["properties"]!["minimumApiMinor"]!["maximum"] = BGCS.Contracts.Versioning.ContractVersion.C_API_MINOR;
        root["properties"]!["plugin"]!["properties"]!["services"]!["minItems"] = 1;
        root["properties"]!["assets"]!["items"]!["properties"]!["digest"]!["properties"]!["value"]!["pattern"] = "^[0-9a-f]{64}$";
        return root;
    }

    /// <summary>Creates a strict editor schema for the implemented grouped configuration contract.</summary>
    /// <returns>The independent schema JSON document.</returns>
    public JsonObject CreateBindingSchema()
    {
        JsonSerializerOptions options = BindingConfigurationLoader.CreateSerializerOptions();
        options.MakeReadOnly(populateMissingResolver: true);
        JsonNode document = options.GetJsonSchemaAsNode(typeof(BindingConfiguration), new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = (
                context,
                node
            ) => {
                if (node is JsonObject schema && context.PropertyInfo is { Get: not null } property)
                {
                    object? owner = property.DeclaringType.GetConstructor(Type.EmptyTypes) is not null ? Activator.CreateInstance(property.DeclaringType) : null;
                    object? value = owner is null ? null : property.Get(owner);
                    if (owner is not null && (property.PropertyType.IsPrimitive || property.PropertyType.IsEnum || property.PropertyType == typeof(string)
                        || property.PropertyType.IsGenericType && property.PropertyType.GetGenericTypeDefinition() == typeof(ImmutableArray<>)))
                        schema["default"] = JsonSerializer.SerializeToNode(value, property.PropertyType, options);
                }
                return node;
            }
        });
        JsonObject root = (JsonObject)document;
        root["$schema"] = "https://json-schema.org/draft/2020-12/schema";
        root["title"] = "BGCS 3.0 grouped binding configuration";
        JsonObject properties = (JsonObject)root["properties"]!;
        properties["extends"] = new JsonObject
        {
            ["type"] = "array",
            ["default"] = new JsonArray(),
            ["items"] = new JsonObject
            {
                ["oneOf"] = new JsonArray(
                new JsonObject { ["type"] = "string", ["minLength"] = 1, ["not"] = new JsonObject { ["pattern"] = "://" } },
                new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["required"] = new JsonArray("url", "sha256", "pathBase"),
                    ["properties"] = new JsonObject
                    {
                        ["url"] = new JsonObject { ["type"] = "string", ["format"] = "uri", ["pattern"] = "^[hH][tT][tT][pP][sS]://[^/?#@]+(?:[/?][^#]*)?$" },
                        ["sha256"] = new JsonObject { ["type"] = "string", ["pattern"] = "^[0-9a-fA-F]{64}$" },
                        ["pathBase"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["minLength"] = 1,
                            ["not"] = new JsonObject { ["pattern"] = "://" },
                            ["description"] = "Explicit local base for paths inside the remote document, relative to the declaring document's local base or absolute."
                        }
                    }
                })
            }
        };
        JsonObject version = (JsonObject)properties["schemaVersion"]!;
        version["const"] = 1;
        version.Remove("default");
        root["required"] = new JsonArray("schemaVersion");
        root["anyOf"] = new JsonArray(
            new JsonObject
            {
                ["required"] = new JsonArray("library", "source"),
                ["properties"] = new JsonObject
                { ["library"] = new JsonObject { ["required"] = new JsonArray("id", "nativeName") }, ["source"] = new JsonObject { ["required"] = new JsonArray("headers") } }
            },
            new JsonObject { ["required"] = new JsonArray("extends"), ["properties"] = new JsonObject { ["extends"] = new JsonObject { ["minItems"] = 1 } } },
            new JsonObject { ["required"] = new JsonArray("profile"), ["properties"] = new JsonObject { ["profile"] = new JsonObject { ["type"] = "string", ["minLength"] = 1 } } });
        JsonObject library = (JsonObject)properties["library"]!;
        foreach (string name in new[] { "id", "nativeName" })
        {
            JsonObject identity = (JsonObject)library["properties"]![name]!;
            identity["minLength"] = 1;
            identity.Remove("default");
        }
        JsonObject source = (JsonObject)properties["source"]!;
        ((JsonObject)source["properties"]!["headers"]!)["minItems"] = 1;
        ((JsonObject)source["properties"]!["headers"]!).Remove("default");
        ((JsonObject)source["properties"]!["language"]!)["enum"] = new JsonArray("c", "cpp");
        ((JsonObject)source["properties"]!["languageStandard"]!)["description"] = "Defaults to c17 for C and c++23 for C++ when omitted or null.";
        Constrain("runtime", "mode", "package", "source");
        Constrain("output", "layout", "split", "singleFile");
        JsonObject sourceProcessors = (JsonObject)properties["output"]!["properties"]!["processors"]!;
        sourceProcessors["maxItems"] = 128;
        sourceProcessors["items"]!["properties"]!["timeoutMilliseconds"]!["minimum"] = 1;
        sourceProcessors["items"]!["properties"]!["timeoutMilliseconds"]!["maximum"] = 30000;
        sourceProcessors["items"]!["properties"]!["paths"]!["minItems"] = 1;
        sourceProcessors["items"]!["properties"]!["paths"]!["maxItems"] = 64;
        sourceProcessors["items"]!["properties"]!["paths"]!["uniqueItems"] = true;
        Constrain("nativeBuild", "mode", "none", "sources", "prebuilt");
        ((JsonObject)properties["cache"]!["properties"]!["maximumBytes"]!)["minimum"] = 1;
        ((JsonObject)properties["cache"]!["properties"]!["maximumEntryBytes"]!)["minimum"] = 1;
        ((JsonObject)properties["cache"]!["properties"]!["maximumEntryBytes"]!)["maximum"] = 268435456;
        ((JsonObject)properties["semantics"]!["properties"]!["ownedResults"]!["items"]!["properties"]!["releaseThreadDomain"]!)["enum"] = new JsonArray("any", "unknown");
        JsonObject virtualCleanup = (JsonObject)properties["cpp"]!["properties"]!["virtualCallbacks"]!["items"]!["properties"]!["releaseThreadDomain"]!;
        virtualCleanup["enum"] = new JsonArray("unknown", "any");
        virtualCleanup["default"] = "unknown";
        virtualCleanup["description"] = "unknown preserves the raw native proxy API; any explicitly asserts that destruction is valid on every closing thread. This remains UserAsserted and does not establish native retention or stop-and-join quiescence.";
        ((JsonObject)properties["api"]!["properties"]!["framework"]!)["const"] = "net10.0";
        properties["api"]!["properties"]!["maximumValueStringCodeUnits"]!["minimum"] = 1;
        properties["semantics"]!["properties"]!["stringParameters"]!["items"]!["properties"]!["maximumCodeUnits"]!["minimum"] = 1;
        JsonObject enumDefinition = (JsonObject)properties["api"]!["properties"]!["enums"]!["items"]!;
        enumDefinition["required"] = new JsonArray("name", "underlyingType", "values");
        enumDefinition["properties"]!["name"]!["minLength"] = 1;
        enumDefinition["properties"]!["underlyingType"]!["enum"] = new JsonArray(
            "int8", "uInt8", "int16", "uInt16", "int32", "uInt32", "int64", "uInt64");
        enumDefinition["properties"]!["values"]!["items"]!["required"] = new JsonArray("name", "value");
        enumDefinition["properties"]!["values"]!["items"]!["properties"]!["name"]!["minLength"] = 1;
        enumDefinition["properties"]!["values"]!["items"]!["properties"]!["value"]!["pattern"] = "^-?(0|[1-9][0-9]*)$";
        foreach (string timeout in new[] { "sessionTimeoutSeconds", "managedCompilationTimeoutSeconds", "nativeBuildTimeoutSeconds" })
        {
            JsonObject schema = (JsonObject)properties["execution"]!["properties"]![timeout]!;
            schema["minimum"] = 1;
            schema["maximum"] = 4_294_967;
        }
        JsonObject diagnosticCodes = (JsonObject)properties["execution"]!["properties"]!["diagnostics"]!["properties"]!["errorCodes"]!;
        diagnosticCodes["uniqueItems"] = true;
        diagnosticCodes["items"]!["minLength"] = 1;
        diagnosticCodes["items"]!["maxLength"] = 256;
        diagnosticCodes["items"]!["pattern"] = "^[^\\s\\u0000-\\u001f\\u007f-\\u009f]+$";
        ((JsonObject)properties["targets"]!)["minItems"] = 1;
        JsonObject fragment = (JsonObject)root.DeepClone();
        fragment.Remove("anyOf");
        ((JsonObject)fragment["properties"]!["library"]!).Remove("required");
        ((JsonObject)fragment["properties"]!["source"]!).Remove("required");
        JsonObject preset = (JsonObject)fragment.DeepClone();
        preset.Remove("$schema");
        preset.Remove("title");
        preset.Remove("required");
        JsonObject presetProperties = (JsonObject)preset["properties"]!;
        foreach (string name in new[] { "schemaVersion", "$schema", "extends", "profile", "profiles" }) presetProperties.Remove(name);
        ((JsonObject)properties["profiles"]!)["additionalProperties"] = preset.DeepClone();
        ((JsonObject)fragment["properties"]!["profiles"]!)["additionalProperties"] = preset;
        root["$defs"] = new JsonObject { ["configurationFragment"] = fragment };
        return root;

        void Constrain(
            string group,
            string name,
            params string[] values
        ) {
            JsonArray choices = new();
            foreach (string value in values) choices.Add(value);
            ((JsonObject)properties[group]!["properties"]![name]!)["enum"] = choices;
        }
    }
}
