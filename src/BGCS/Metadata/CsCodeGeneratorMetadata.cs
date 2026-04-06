namespace BGCS.Metadata
{
    using BGCS.Core.CSharp;

    using Newtonsoft.Json;
    using Newtonsoft.Json.Converters;
    using System.Diagnostics.CodeAnalysis;

    /// <summary>
    /// Defines the public class <c>CsCodeGeneratorMetadata</c>.
    /// </summary>
    public class CsCodeGeneratorMetadata
    {
        private readonly Dictionary<string, GeneratorMetadataEntry> entries = [];

        /// <summary>
        /// Gets or sets <c>Settings</c>.
        /// </summary>
        public CsCodeGeneratorConfig Settings { get; set; } = null!;

        /// <summary>
        /// Exposes public member <c>entries</c>.
        /// </summary>
        public Dictionary<string, GeneratorMetadataEntry> Entries => entries;

        /// <summary>
        /// Exposes public member <c>index]</c>.
        /// </summary>
        public GeneratorMetadataEntry this[string index]
        {
            get => Entries[index];
            set => Entries[index] = value;
        }

        /// <summary>
        /// Exposes public member <c>DefinedConstants</c>.
        /// </summary>
        public List<CsConstantMetadata> DefinedConstants
        {
            get => GetOrCreate<MetadataListEntry<CsConstantMetadata>>("DefinedConstants").Values;
            set => entries["DefinedConstants"] = new MetadataListEntry<CsConstantMetadata>(value);
        }

        /// <summary>
        /// Exposes public member <c>DefinedEnums</c>.
        /// </summary>
        public List<CsEnumMetadata> DefinedEnums
        {
            get => GetOrCreate<MetadataListEntry<CsEnumMetadata>>("DefinedEnums").Values;
            set => entries["DefinedEnums"] = new MetadataListEntry<CsEnumMetadata>(value);
        }

        /// <summary>
        /// Exposes public member <c>DefinedExtensionTypes</c>.
        /// </summary>
        public List<string> DefinedExtensionTypes
        {
            get => GetOrCreate<MetadataListEntry<string>>("DefinedExtensionTypes").Values;
            set => entries["DefinedExtensionTypes"] = new MetadataListEntry<string>(value);
        }

        /// <summary>
        /// Exposes public member <c>DefinedExtensions</c>.
        /// </summary>
        public List<CsFunction> DefinedExtensions
        {
            get => GetOrCreate<MetadataListEntry<CsFunction>>("DefinedExtensions").Values;
            set => entries["DefinedExtensions"] = new MetadataListEntry<CsFunction>(value);
        }

        /// <summary>
        /// Exposes public member <c>DefinedCOMExtensionTypes</c>.
        /// </summary>
        public List<string> DefinedCOMExtensionTypes
        {
            get => GetOrCreate<MetadataListEntry<string>>("DefinedCOMExtensionTypes").Values;
            set => entries["DefinedCOMExtensionTypes"] = new MetadataListEntry<string>(value);
        }

        /// <summary>
        /// Exposes public member <c>DefinedCOMExtensions</c>.
        /// </summary>
        public Dictionary<string, HashSet<CsFunctionVariation>> DefinedCOMExtensions
        {
            get => GetOrCreate<MetadataDictionaryEntry<string, HashSet<CsFunctionVariation>>>("DefinedCOMExtensions").Dictionary;
            set => entries["DefinedCOMExtensions"] = new MetadataDictionaryEntry<string, HashSet<CsFunctionVariation>>(value);
        }

        /// <summary>
        /// Exposes public member <c>CppDefinedFunctions</c>.
        /// </summary>
        public List<string> CppDefinedFunctions
        {
            get => GetOrCreate<MetadataListEntry<string>>("CppDefinedFunctions").Values;
            set => entries["CppDefinedFunctions"] = new MetadataListEntry<string>(value);
        }

        /// <summary>
        /// Exposes public member <c>DefinedFunctions</c>.
        /// </summary>
        public List<CsFunction> DefinedFunctions
        {
            get => GetOrCreate<MetadataListEntry<CsFunction>>("DefinedFunctions").Values;
            set => entries["DefinedFunctions"] = new MetadataListEntry<CsFunction>(value);
        }

        /// <summary>
        /// Exposes public member <c>DefinedTypedefs</c>.
        /// </summary>
        public List<string> DefinedTypedefs
        {
            get => GetOrCreate<MetadataListEntry<string>>("DefinedTypedefs").Values;
            set => entries["DefinedTypedefs"] = new MetadataListEntry<string>(value);
        }

        /// <summary>
        /// Exposes public member <c>DefinedTypes</c>.
        /// </summary>
        public List<string> DefinedTypes
        {
            get => GetOrCreate<MetadataListEntry<string>>("DefinedTypes").Values;
            set => entries["DefinedTypes"] = new MetadataListEntry<string>(value);
        }

        /// <summary>
        /// Exposes public member <c>DefinedDelegates</c>.
        /// </summary>
        public List<CsDelegate> DefinedDelegates
        {
            get => GetOrCreate<MetadataListEntry<CsDelegate>>("DefinedDelegates").Values;
            set => entries["DefinedDelegates"] = new MetadataListEntry<CsDelegate>(value);
        }

        /// <summary>
        /// Exposes public member <c>WrappedPointers</c>.
        /// </summary>
        public Dictionary<string, string> WrappedPointers
        {
            get => GetOrCreate<MetadataDictionaryEntry<string, string>>("WrappedPointers").Dictionary;
            set => entries["WrappedPointers"] = new MetadataDictionaryEntry<string, string>(value);
        }

        /// <summary>
        /// Exposes public member <c>FunctionTable</c>.
        /// </summary>
        public CsFunctionTableMetadata FunctionTable
        {
            get => GetOrCreate<CsFunctionTableMetadata>("FunctionTable");
            set => entries["FunctionTable"] = value;
        }

        /// <summary>
        /// Executes public operation <c>ContainsKey</c>.
        /// </summary>
        public bool ContainsKey(string key)
        {
            return Entries.ContainsKey(key);
        }

        /// <summary>
        /// Attempts to resolve data via <c>TryGetEntry</c> without throwing.
        /// </summary>
        public bool TryGetEntry(string key, [NotNullWhen(true)] out GeneratorMetadataEntry? entry)
        {
            return Entries.TryGetValue(key, out entry);
        }

        /// <summary>
        /// Attempts to resolve data via <c>TryGetEntry</c> without throwing.
        /// </summary>
        public bool TryGetEntry<T>(string key, [NotNullWhen(true)] out T? entry) where T : GeneratorMetadataEntry
        {
            bool result = Entries.TryGetValue(key, out var metadataEntry);
            if (result && metadataEntry is T t)
            {
                entry = t;
                return true;
            }
            entry = default;
            return false;
        }

        /// <summary>
        /// Returns computed data from <c>GetEntry</c>.
        /// </summary>
        public T? GetEntry<T>(string key) where T : GeneratorMetadataEntry
        {
            bool result = Entries.TryGetValue(key, out var metadataEntry);
            if (result && metadataEntry is T t)
            {
                return t;
            }

            return default;
        }

        /// <summary>
        /// Returns computed data from <c>GetOrCreate</c>.
        /// </summary>
        public T GetOrCreate<T>(string key) where T : GeneratorMetadataEntry, new()
        {
            T entryT;
            if (TryGetEntry(key, out var entry))
            {
                if (entry is T t)
                {
                    return t;
                }
            }

            entryT = new T();
            Entries[key] = entryT;
            return entryT;
        }

        /// <summary>
        /// Merges configuration or metadata via <c>Merge</c>.
        /// </summary>
        public void Merge(CsCodeGeneratorMetadata from, in MergeOptions options)
        {
            foreach (var item in from.Entries)
            {
                if (TryGetEntry(item.Key, out var entry))
                {
                    entry.Merge(item.Value, options);
                }
            }
        }

        /// <summary>
        /// Executes public operation <c>Clone</c>.
        /// </summary>
        public CsCodeGeneratorMetadata Clone(bool shallow = false)
        {
            CsCodeGeneratorMetadata metadata = new();
            metadata.Settings = Settings;
            foreach (var item in Entries)
            {
                if (shallow)
                {
                    metadata.Entries[item.Key] = item.Value;
                }
                else
                {
                    metadata.Entries[item.Key] = item.Value.Clone();
                }
            }
            return metadata;
        }

        private static readonly JsonSerializerSettings options = new()
        {
            Formatting = Formatting.Indented,
            Converters = { new StringEnumConverter() }
        };

        private static readonly JsonSerializer serializer = JsonSerializer.Create(options);

        /// <summary>
        /// Persists data using <c>Save</c>.
        /// </summary>
        public void Save(string path)
        {
            using var fs = File.CreateText(path);
            using JsonTextWriter writer = new(fs);
            serializer.Serialize(writer, this);
        }

        /// <summary>
        /// Loads resources or metadata using <c>Load</c>.
        /// </summary>
        public static CsCodeGeneratorMetadata Load(string path)
        {
            using var fs = File.OpenText(path);
            using JsonTextReader reader = new(fs);
            return serializer.Deserialize<CsCodeGeneratorMetadata>(reader) ?? new();
        }
    }
}
