using System;
using System.IO;

namespace BGCS.Cpp2C.Configuration
{
    using BGCS.Core.Configuration;
    using BGCS.Core.Extensibility;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;

    /// <summary>
    /// Loads, composes, and serializes declarative C++ bridge policy while excluding loaded runtime services.
    /// </summary>
    public partial class Cpp2CGeneratorConfig
    {
        private static readonly JsonSerializerSettings SerializationSettings = new()
        {
            DefaultValueHandling = DefaultValueHandling.Ignore,
            Formatting = Formatting.Indented,
        };
        /// <summary>
        /// Gets the absolute directory of the loaded configuration, used to resolve relative paths without changing
        /// the process current directory. It is null for configurations created directly in memory.
        /// </summary>
        [JsonIgnore, System.Text.Json.Serialization.JsonIgnore]
        public string? configDirectory { get; private set; }

        /// <summary>
        /// Loads and composes a C++ bridge configuration without rewriting an existing source file.
        /// </summary>
        /// <param name = "file">Configuration file path.</param>
        /// <param name = "composer">Optional custom base configuration composer.</param>
        /// <returns>The composed configuration with its source directory recorded for relative path resolution.</returns>
        public static Cpp2CGeneratorConfig Load(
            string file,
            IConfigComposer? composer = null
        ) {
            string fullPath = Path.GetFullPath(file);
            bool exists = File.Exists(fullPath);
            if (!exists)
                throw new FileNotFoundException($"Configuration file not found: {fullPath}", fullPath);
            string configDirectory = Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory;
            JObject document = JObject.Parse(File.ReadAllText(fullPath));
            Cpp2CGeneratorConfig result;
            if (composer == null)
            {
                result = JsonConfigurationComposer.Compose(document, configDirectory, fullPath)
                    .ToObject<Cpp2CGeneratorConfig>()
                    ?? throw new InvalidOperationException($"Unable to deserialize bridge configuration '{fullPath}'.");
            }
            else
            {
                result = document.ToObject<Cpp2CGeneratorConfig>()
                    ?? throw new InvalidOperationException($"Unable to deserialize bridge configuration '{fullPath}'.");
                composer.Compose(ref result, configDirectory);
            }
            result.configDirectory = configDirectory;
            Cpp2CConfigValidator.Validate(result);
            foreach (string pluginAssembly in result.pluginAssemblies)
                BindingPluginLoader.Load(Path.GetFullPath(pluginAssembly, configDirectory), result.plugins);
            return result;
        }

        /// <summary>
        /// Writes the declarative bridge configuration to a file, replacing existing contents.
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

        /// <summary>
        /// Serializes the declarative bridge configuration without runtime extension instances.
        /// </summary>
        /// <returns>
        /// An indented JSON document using the current configuration property names.
        /// </returns>
        public string Serialize() => JsonConvert.SerializeObject(this, SerializationSettings);

    }
}
