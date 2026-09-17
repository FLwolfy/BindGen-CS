namespace BGCS.Cpp2C
{
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

        [JsonIgnore]
        internal string? ConfigDirectory { get; private set; }

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
            string previousDirectory = Environment.CurrentDirectory;
            try
            {
                result.ConfigDirectory = Path.GetDirectoryName(fullPath) ?? previousDirectory;
                Environment.CurrentDirectory = result.ConfigDirectory;
                composer.Compose(ref result);
                result.ConfigDirectory = Path.GetDirectoryName(fullPath) ?? previousDirectory;
                return result;
            }
            finally
            {
                Environment.CurrentDirectory = previousDirectory;
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
