using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using BGCS.Contracts.Common;

namespace BGCS.Configuration;

/// <summary>Names a local base document or explicitly pinned HTTPS document with an explicit local path base.</summary>
[JsonConverter(typeof(ConfigurationReferenceConverter))]
public sealed record ConfigurationReference
{
    private ConfigurationReference(
        string? path,
        Uri? url,
        ContentDigest? digest,
        string? pathBase
    ) { this.path = path; this.url = url; this.digest = digest; this.pathBase = pathBase; }

    /// <summary>Gets a local path resolved from the declaring document's local path base.</summary>
    public string? path { get; }
    /// <summary>Gets the explicit absolute HTTPS input address.</summary>
    public Uri? url { get; }
    /// <summary>Gets the required SHA-256 of the exact remote bytes.</summary>
    public ContentDigest? digest { get; }
    /// <summary>Gets the explicitly declared local base for paths inside a remote document.</summary>
    public string? pathBase { get; }

    /// <summary>Creates a local base reference without interpreting a URL as a filesystem path.</summary>
    /// <param name="path">The local relative or absolute path.</param>
    /// <returns>The immutable reference.</returns>
    public static ConfigurationReference Local(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.Contains("://", StringComparison.Ordinal)) throw new ArgumentException("Remote configuration references require an explicit HTTPS url, sha256 and local pathBase.", nameof(path));
        return new(path, null, null, null);
    }

    /// <summary>Creates a content-pinned remote reference whose local paths have an explicit meaning.</summary>
    /// <param name="url">The absolute HTTPS document address, without a URI fragment.</param>
    /// <param name="sha256">The exact SHA-256 content identity.</param>
    /// <param name="pathBase">The local path base, relative to the declaring document's local base or absolute.</param>
    /// <returns>The immutable validated reference.</returns>
    public static ConfigurationReference Https(
        Uri url,
        ContentDigest sha256,
        string pathBase
    ) {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(pathBase);
        if (pathBase.Contains("://", StringComparison.Ordinal))
            throw new ArgumentException("A remote configuration pathBase must name a local directory, never a URL directory.", nameof(pathBase));
        if (!url.IsAbsoluteUri || url.Scheme != Uri.UriSchemeHttps || url.Fragment.Length != 0 || url.UserInfo.Length != 0)
            throw new ArgumentException("A remote configuration must use an absolute HTTPS URL without a fragment or embedded credentials.", nameof(url));
        _ = new ContentDigest(sha256.value);
        return new(null, url, sha256, pathBase);
    }

    /// <summary>Creates the concise local reference used in configuration arrays.</summary>
    /// <param name="path">The local base path.</param>
    /// <returns>The validated local reference; invalid or empty paths throw an argument exception.</returns>
    public static implicit operator ConfigurationReference(string path) => Local(path);
}

/// <summary>Serializes concise local references and strict pinned HTTPS objects.</summary>
public sealed class ConfigurationReferenceConverter : JsonConverter<ConfigurationReference>
{
    /// <inheritdoc/>
    public override ConfigurationReference Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) {
        if (reader.TokenType == JsonTokenType.String) return ConfigurationReference.Local(reader.GetString()!);
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("A base reference must be a local path or a pinned HTTPS object.");
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement value = document.RootElement;
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
            if (property.Name is not ("url" or "sha256" or "pathBase") || !names.Add(property.Name) || property.Value.ValueKind != JsonValueKind.String)
                throw new JsonException("Pinned HTTPS references accept exactly one string url, sha256 and pathBase.");
        if (names.Count != 3) throw new JsonException("A pinned HTTPS reference requires url, sha256 and pathBase.");
        try
        {
            return ConfigurationReference.Https(new Uri(value.GetProperty("url").GetString()!, UriKind.Absolute),
                new ContentDigest(value.GetProperty("sha256").GetString()!.ToLowerInvariant()), value.GetProperty("pathBase").GetString()!);
        }
        catch (Exception failure) when (failure is ArgumentException or UriFormatException)
        { throw new JsonException("The pinned HTTPS configuration reference is invalid: " + failure.Message, failure); }
    }

    /// <inheritdoc/>
    public override void Write(
        Utf8JsonWriter writer,
        ConfigurationReference value,
        JsonSerializerOptions options
    ) {
        if (value.path is string local) { writer.WriteStringValue(local); return; }
        writer.WriteStartObject();
        writer.WriteString("url", value.url!.AbsoluteUri);
        writer.WriteString("sha256", value.digest!.Value.value);
        writer.WriteString("pathBase", value.pathBase);
        writer.WriteEndObject();
    }
}

/// <summary>Records verified bytes of an explicit remote configuration without treating a URL as a local file dependency.</summary>
/// <param name="url">The requested pinned HTTPS document identity.</param>
/// <param name="digest">The verified complete response digest.</param>
/// <param name="length">The verified byte length.</param>
/// <param name="pathBase">The resolved explicit local path base used during composition.</param>
public sealed record PinnedConfigurationInput(
    Uri url,
    ContentDigest digest,
    long length,
    string pathBase
);
