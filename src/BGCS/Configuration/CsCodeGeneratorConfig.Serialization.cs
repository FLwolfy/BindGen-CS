using System;
using System.IO;
using BGCS.Core.Extensibility;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BGCS.Configuration;

/// <summary>
/// Serializes declarative binding policy while excluding loaded runtime services and attempt-local native data.
/// </summary>
public partial class CsCodeGeneratorConfig
{
    private static readonly JsonSerializerSettings SerializationSettings = new()
    {
        DefaultValueHandling = DefaultValueHandling.Ignore,
        Formatting = Formatting.Indented,
        Converters = [new StringEnumConverter()]
    };

    /// <summary>
    /// Serializes the declarative configuration without runtime services or loaded plugin instances.
    /// </summary>
    /// <returns>
    /// An indented JSON document using the current configuration property names.
    /// </returns>
    public string Serialize() => JsonConvert.SerializeObject(this, SerializationSettings);

    /// <summary>
    /// Writes the declarative configuration to a file, replacing existing contents.
    /// </summary>
    /// <param name="path">
    /// Destination file path; its parent directory must already exist.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The destination path is empty or contains only whitespace.
    /// </exception>
    /// <exception cref="IOException">
    /// The configuration cannot be written to the destination.
    /// </exception>
    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.WriteAllText(path, Serialize());
    }

    [JsonIgnore]
    internal string? configDirectory { get; set; }

    [JsonIgnore]
    internal bool presetDefaultsApplied { get; set; }

    internal static JsonSerializer CreateMergeSerializer() => JsonSerializer.Create(new JsonSerializerSettings
    {
        DefaultValueHandling = DefaultValueHandling.Ignore
    });

    internal void LoadConfiguredPlugins()
    {
        if (pluginAssemblies == null)
            throw new InvalidOperationException("pluginAssemblies cannot be null.");
        string baseDirectory = configDirectory ?? Environment.CurrentDirectory;
        foreach (string pluginAssembly in pluginAssemblies)
        {
            if (string.IsNullOrWhiteSpace(pluginAssembly))
                throw new InvalidOperationException("pluginAssemblies cannot contain an empty path.");
            string fullPath = Path.GetFullPath(pluginAssembly, baseDirectory);
            if (loadedPluginAssemblies.Contains(fullPath))
                continue;
            BindingPluginLoader.Load(fullPath, plugins);
            loadedPluginAssemblies.Add(fullPath);
        }
    }
}
