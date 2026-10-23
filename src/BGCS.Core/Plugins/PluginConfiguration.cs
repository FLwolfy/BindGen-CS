using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BGCS.Core.Plugins;

/// <summary>Owns immutable plugin settings and the captured base for explicitly declared relative inputs.</summary>
public sealed class PluginConfiguration
{
    private readonly string m_defaultPathBase;
    /// <summary>Copies settings independently of the caller's JSON document lifetime.</summary>
    /// <param name="id">The owning plugin identity.</param>
    /// <param name="settings">The schema-validated settings object.</param>
    /// <param name="pathBase">The captured absolute local input base.</param>
    /// <param name="valuePathBases">Optional JSON-pointer origins for settings composed from multiple documents.</param>
    public PluginConfiguration(
        string id,
        JsonElement settings,
        string pathBase,
        ImmutableDictionary<string, string>? valuePathBases = null
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(pathBase);
        if (settings.ValueKind != JsonValueKind.Object) throw new ArgumentException("Plugin settings must be an object.", nameof(settings));
        this.id = id;
        this.settings = settings.Clone();
        if (!Path.IsPathFullyQualified(pathBase)) throw new ArgumentException("Plugin path bases must be absolute.", nameof(pathBase));
        m_defaultPathBase = Path.GetFullPath(pathBase);
        this.valuePathBases = valuePathBases ?? ImmutableDictionary<string, string>.Empty;
        foreach ((string pointer, string origin) in this.valuePathBases)
        {
            _ = GetValue(pointer);
            if (!Path.IsPathFullyQualified(origin)) throw new ArgumentException("Every plugin value origin must be absolute.", nameof(valuePathBases));
        }
    }
    /// <summary>Gets the owning plugin identity.</summary>
    public string id { get; }
    /// <summary>Gets the detached immutable settings object.</summary>
    public JsonElement settings { get; }
    /// <summary>Gets the common origin, or fails when merged settings have different origins.</summary>
    /// <exception cref="InvalidOperationException">Composed values require a specific JSON pointer.</exception>
    public string pathBase => GetPathBase(string.Empty);
    /// <summary>Gets the explicitly captured fallback base for SDK settings without individual origins.</summary>
    public string defaultPathBase => m_defaultPathBase;
    /// <summary>Gets immutable JSON-pointer origins retained through configuration composition.</summary>
    public ImmutableDictionary<string, string> valuePathBases { get; }

    /// <summary>Gets a value's declaring input base without rebasing inherited relative paths.</summary>
    /// <param name="jsonPointer">An RFC 6901 pointer to an existing settings value.</param>
    /// <returns>The value's captured absolute local path base.</returns>
    /// <exception cref="ArgumentException">The pointer is invalid or identifies no value.</exception>
    /// <exception cref="InvalidOperationException">The selected object contains values from different origins.</exception>
    public string GetPathBase(string jsonPointer)
    {
        _ = GetValue(jsonPointer);
        string candidate = jsonPointer;
        while (true)
        {
            if (valuePathBases.TryGetValue(candidate, out string? origin)) return origin;
            if (candidate.Length == 0) break;
            candidate = candidate[..candidate.LastIndexOf('/')];
        }
        string prefix = jsonPointer.Length == 0 ? "/" : jsonPointer + "/";
        string[] origins = valuePathBases.Where(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal)).Select(pair => pair.Value)
            .Distinct(StringComparer.Ordinal).ToArray();
        return origins.Length switch { 0 => m_defaultPathBase, 1 => origins[0], _ => throw new InvalidOperationException("Plugin settings have multiple input bases; select the specific value with GetPathBase(jsonPointer).") };
    }

    /// <summary>Resolves one explicitly declared local path using that value's original document.</summary>
    /// <param name="jsonPointer">An RFC 6901 pointer to a string settings value.</param>
    /// <returns>The absolute local input path.</returns>
    public string ResolveInputPath(string jsonPointer)
    {
        string value = GetValue(jsonPointer).GetString() ?? throw new ArgumentException("A local plugin input path must be a string.", nameof(jsonPointer));
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return Path.GetFullPath(value, GetPathBase(jsonPointer));
    }

    private JsonElement GetValue(string pointer)
    {
        ArgumentNullException.ThrowIfNull(pointer);
        if (pointer.Length != 0 && pointer[0] != '/') throw new ArgumentException("A settings pointer must be empty or start with '/'.", nameof(pointer));
        JsonElement value = settings;
        if (pointer.Length == 0) return value;
        foreach (string part in pointer[1..].Split('/'))
        {
            for (int escapeIndex = 0; escapeIndex < part.Length; escapeIndex++)
                if (part[escapeIndex] == '~' && (++escapeIndex >= part.Length || part[escapeIndex] is not ('0' or '1'))) throw new ArgumentException("A settings pointer has an invalid escape.", nameof(pointer));
            string name = part.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out JsonElement property)) value = property;
            else if (value.ValueKind == JsonValueKind.Array && int.TryParse(name, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int index)
                && (name.Length == 1 || name[0] != '0') && index < value.GetArrayLength()) value = value[index];
            else throw new ArgumentException("A settings pointer identifies no value.", nameof(pointer));
        }
        return value;
    }
}
