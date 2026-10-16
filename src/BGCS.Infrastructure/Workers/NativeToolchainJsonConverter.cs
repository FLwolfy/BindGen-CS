using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using BGCS.Contracts.Targets;

namespace BGCS.Infrastructure.Workers;

internal sealed class NativeToolchainJsonConverter : JsonConverter<NativeToolchainDescriptor>
{
    public override NativeToolchainDescriptor Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;
        return new(ReadString(root, "compilerPath"), ReadString(root, "sysRoot"),
            ReadArray(root, "systemIncludeFolders"), ReadArray(root, "defines"),
            ReadArray(root, "arguments"), ReadArray(root, "cxxSystemIncludeFolders"),
            ReadString(root, "sdkVersion"), ReadString(root, "compilerVersion"), ReadString(root, "linkerPath"), ReadArray(root, "libraryDirectories"));
    }

    public override void Write(
        Utf8JsonWriter writer,
        NativeToolchainDescriptor value,
        JsonSerializerOptions options
    ) {
        writer.WriteStartObject();
        writer.WriteString("compilerPath", value.compilerPath);
        writer.WriteString("sysRoot", value.sysRoot);
        writer.WriteString("sdkVersion", value.sdkVersion);
        writer.WriteString("compilerVersion", value.compilerVersion);
        writer.WriteString("linkerPath", value.linkerPath);
        WriteArray(writer, "libraryDirectories", value.libraryDirectories);
        WriteArray(writer, "systemIncludeFolders", value.systemIncludeFolders);
        WriteArray(writer, "cxxSystemIncludeFolders", value.cxxSystemIncludeFolders);
        WriteArray(writer, "defines", value.defines);
        WriteArray(writer, "arguments", value.arguments);
        writer.WriteEndObject();
    }

    private static string? ReadString(
        JsonElement root,
        string name
    ) => root.TryGetProperty(name, out JsonElement value) && value.ValueKind != JsonValueKind.Null
        ? value.GetString() : null;

    private static string[] ReadArray(
        JsonElement root,
        string name
    ) {
        if (!root.TryGetProperty(name, out JsonElement value))
            return [];
        List<string> result = [];
        foreach (JsonElement item in value.EnumerateArray())
            result.Add(item.GetString() ?? throw new JsonException("A toolchain input cannot be null."));
        return result.ToArray();
    }

    private static void WriteArray(
        Utf8JsonWriter writer,
        string name,
        IReadOnlyList<string> values
    ) {
        writer.WriteStartArray(name);
        foreach (string value in values)
            writer.WriteStringValue(value);
        writer.WriteEndArray();
    }
}
