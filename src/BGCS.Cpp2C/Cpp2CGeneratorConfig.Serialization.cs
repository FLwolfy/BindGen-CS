namespace BGCS.Cpp2C
{
    using BGCS.Cpp2C.Configuration;
    using BGCS.Core.Extensibility;
    using Newtonsoft.Json;


    /// <summary>
    /// Defines the public class <c>Cpp2CGeneratorConfig</c> used by the generation pipeline.
    /// </summary>
    public partial class Cpp2CGeneratorConfig
    {
        /// <summary>
        /// Performs the operation implemented by <c>new</c>.
        /// </summary>
        /// <returns>Result produced by <c>new</c>.</returns>
        public static readonly JsonSerializerSettings SerializerSettings = new()
        {
            DefaultValueHandling = DefaultValueHandling.Ignore,
            Formatting = Formatting.Indented,
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

        /// <summary>
        /// Gets the absolute directory of the loaded configuration, used to resolve relative paths without changing
        /// the process current directory. It is null for configurations created directly in memory.
        /// </summary>
        [JsonIgnore, System.Text.Json.Serialization.JsonIgnore]
        public string? ConfigDirectory { get; private set; }

        /// <summary>
        /// Loads and composes a C++ bridge configuration without rewriting an existing source file.
        /// </summary>
        /// <param name="file">Configuration file path.</param>
        /// <param name="composer">Optional custom base configuration composer.</param>
        /// <returns>The composed configuration with its source directory recorded for relative path resolution.</returns>
        public static Cpp2CGeneratorConfig Load(string file, IConfigComposer? composer = null)
        {
            string fullPath = Path.GetFullPath(file);
            bool exists = File.Exists(fullPath);
            Cpp2CGeneratorConfig result = exists
                ? JsonConvert.DeserializeObject<Cpp2CGeneratorConfig>(File.ReadAllText(fullPath)) ?? new()
                : new();
            if (!exists)
                result.Save(fullPath);

            composer ??= new ConfigComposer();
            string configDirectory = Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory;
            result.ConfigDirectory = configDirectory;
            composer.Compose(ref result, configDirectory);
            result.ConfigDirectory = configDirectory;
            Cpp2CConfigValidator.Validate(result);
            foreach (string pluginAssembly in result.PluginAssemblies)
                BindingPluginLoader.Load(Path.GetFullPath(pluginAssembly, configDirectory), result.Plugins);
            return result;
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
