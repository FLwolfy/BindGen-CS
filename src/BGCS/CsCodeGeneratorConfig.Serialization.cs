namespace BGCS
{
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

        /// <summary>
        /// Performs the operation implemented by <c>Load</c>.
        /// </summary>
        /// <returns>Result produced by <c>Load</c>.</returns>
        public static CsCodeGeneratorConfig Load(string file, IConfigComposer? composer = null)
        {
            CsCodeGeneratorConfig result;
            if (File.Exists(file))
            {
                result = JsonConvert.DeserializeObject<CsCodeGeneratorConfig>(File.ReadAllText(file)) ?? new();
            }
            else
            {
                result = new();
            }

            result.Save(file);

            composer ??= new ConfigComposer();
            composer.Compose(ref result);

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
