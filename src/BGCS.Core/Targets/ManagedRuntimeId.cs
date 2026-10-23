using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BGCS.Core.Targets;

/// <summary>Identifies an open managed execution model without a core platform enumeration.</summary>
/// <param name="value">The explicitly registered execution-model identity.</param>
[JsonConverter(typeof(ManagedRuntimeIdJsonConverter))]
public readonly record struct ManagedRuntimeId(string value)
{
    /// <summary>Returns the registered identity.</summary>
    /// <returns>The identity text.</returns>
    public override string ToString() => value;
}

/// <summary>Serializes a managed runtime identity as an exact JSON string.</summary>
public sealed class ManagedRuntimeIdJsonConverter : JsonConverter<ManagedRuntimeId>
{
    /// <inheritdoc />
    public override ManagedRuntimeId Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) {
        string value = reader.TokenType == JsonTokenType.String ? reader.GetString()! : throw new JsonException("A runtime identity must be a string.");
        if (string.IsNullOrWhiteSpace(value)) throw new JsonException("A runtime identity cannot be empty.");
        return new(value);
    }
    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer,
        ManagedRuntimeId value,
        JsonSerializerOptions options
    ) {
        if (string.IsNullOrWhiteSpace(value.value)) throw new JsonException("A runtime identity cannot be empty.");
        writer.WriteStringValue(value.value);
    }
}
