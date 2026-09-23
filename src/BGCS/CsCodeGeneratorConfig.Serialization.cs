namespace BGCS
{
    using BGCS.Configuration;
    using BGCS.Core.Extensibility;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Converters;

    /// <summary>
    /// Defines the public class <c>CsCodeGeneratorConfig</c> used by the generation pipeline.
    /// </summary>
    public partial class CsCodeGeneratorConfig
    {
        /// <summary>
        /// Performs the operation implemented by <c>new</c>.
        /// </summary>
        /// <returns>Result produced by <c>new</c>.</returns>
        public static readonly JsonSerializerSettings SerializerSettings = new()
        {
            DefaultValueHandling = DefaultValueHandling.Ignore,
            Formatting = Formatting.Indented,
            Converters = [new StringEnumConverter()]
        };

        /// <summary>
        /// Performs the operation implemented by <c>Create</c>.
        /// </summary>
        /// <returns>Result produced by <c>Create</c>.</returns>
        public static readonly JsonSerializer Serializer = JsonSerializer.Create(SerializerSettings);

        /// <summary>
        /// Performs the operation implemented by <c>new</c>.
        /// </summary>
        /// <returns>Result produced by <c>new</c>.</returns>
        public static readonly JsonSerializerSettings MergeSerializerSettings = new()
        {
            DefaultValueHandling = DefaultValueHandling.Ignore,
        };

        /// <summary>
        /// Performs the operation implemented by <c>Create</c>.
        /// </summary>
        /// <returns>Result produced by <c>Create</c>.</returns>
        public static readonly JsonSerializer MergeSerializer = JsonSerializer.Create(MergeSerializerSettings);

        [JsonIgnore]
        internal string? ConfigDirectory { get; set; }

        [JsonIgnore]
        internal bool PresetDefaultsApplied { get; set; }

        /// <summary>
        /// Performs the operation implemented by <c>Load</c>.
        /// </summary>
        /// <returns>Result produced by <c>Load</c>.</returns>
        public static CsCodeGeneratorConfig Load(string file, IConfigComposer? composer = null)
        {
            string fullFilePath = Path.GetFullPath(file);
            string? configDirectory = Path.GetDirectoryName(fullFilePath);

            bool fileExists = File.Exists(fullFilePath);
            CsCodeGeneratorConfig result;
            if (fileExists)
            {
                string json = File.ReadAllText(fullFilePath);
                result = JsonConvert.DeserializeObject<CsCodeGeneratorConfig>(json) ?? new();
            }
            else
            {
                result = new();
            }

            if (!fileExists)
            {
                result.Save(fullFilePath);
            }

            composer ??= new ConfigComposer();
            if (composer is IConfigComposerContext contextualComposer)
            {
                contextualComposer.Compose(ref result, configDirectory ?? Environment.CurrentDirectory);
            }
            else
            {
                string previousCwd = Environment.CurrentDirectory;
                try
                {
                    if (!string.IsNullOrEmpty(configDirectory))
                    {
                        Environment.CurrentDirectory = configDirectory;
                    }
                    composer.Compose(ref result);
                }
                finally
                {
                    Environment.CurrentDirectory = previousCwd;
                }
            }
            result.ConfigDirectory = configDirectory;
            result.LoadConfiguredPlugins();
            return result;
        }

        internal void LoadConfiguredPlugins()
        {
            if (PluginAssemblies == null)
                throw new InvalidOperationException("PluginAssemblies cannot be null.");
            string baseDirectory = ConfigDirectory ?? Environment.CurrentDirectory;
            foreach (string pluginAssembly in PluginAssemblies)
            {
                if (string.IsNullOrWhiteSpace(pluginAssembly))
                    throw new InvalidOperationException("PluginAssemblies cannot contain an empty path.");
                string fullPath = Path.GetFullPath(pluginAssembly, baseDirectory);
                if (LoadedPluginAssemblies.Contains(fullPath))
                    continue;
                BindingPluginLoader.Load(fullPath, Plugins);
                LoadedPluginAssemblies.Add(fullPath);
            }
        }

        /// <summary>
        /// Performs the operation implemented by <c>Save</c>.
        /// </summary>
        public void Save(string path)
        {
            File.WriteAllText(path, JsonConvert.SerializeObject(this, SerializerSettings));
        }
    }
}
