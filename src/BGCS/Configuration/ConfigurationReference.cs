using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BGCS.Configuration;

/// <summary>Names a local base document relative to its declaring configuration.</summary>
[JsonConverter(typeof(ConfigurationReferenceConverter))]
public sealed record ConfigurationReference
{
    private ConfigurationReference(string path) => this.path = path;
    /// <summary>Gets the explicit local input path.</summary>
    public string path { get; }
    /// <summary>Creates a local configuration reference.</summary>
    /// <param name="path">The relative or absolute filesystem path.</param>
    /// <returns>A validated local reference.</returns>
    public static ConfigurationReference Local(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.Contains("://", StringComparison.Ordinal)) throw new ArgumentException("Configuration inheritance accepts local files only.", nameof(path));
        return new(path);
    }
    /// <summary>Creates a local configuration reference.</summary>
    /// <param name="path">The local input path.</param>
    /// <returns>A validated local reference.</returns>
    public static implicit operator ConfigurationReference(string path) => Local(path);
}

/// <summary>Reads and writes local configuration reference strings.</summary>
public sealed class ConfigurationReferenceConverter : JsonConverter<ConfigurationReference>
{
    /// <inheritdoc/>
    public override ConfigurationReference Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("A base reference must be a local path string.");
        try { return ConfigurationReference.Local(reader.GetString()!); }
        catch (ArgumentException failure) { throw new JsonException("The local configuration path is invalid.", failure); }
    }
    /// <inheritdoc/>
    public override void Write(
        Utf8JsonWriter writer,
        ConfigurationReference value,
        JsonSerializerOptions options
    )
        => writer.WriteStringValue(value.path);
}
