using System.Collections.Generic;
using BGCS.Configuration;

namespace BGCS.Metadata
{
    using System.Diagnostics.CodeAnalysis;
    using BGCS.CSharp;

    /// <summary>
    /// Collects mutable named generation metadata used by overload planning, patches, and output composition.
    /// </summary>
    public class CsCodeGeneratorMetadata
    {
        private readonly Dictionary<string, GeneratorMetadataEntry> m_entries = [];
        /// <summary>
        /// Gets or sets the generation configuration retained by this metadata container.
        /// </summary>
        public CsCodeGeneratorConfig settings { get; set; } = null!;
        /// <summary>
        /// Gets the mutable named-entry dictionary used by metadata extensions.
        /// </summary>
        public Dictionary<string, GeneratorMetadataEntry> entries => this.m_entries;

        /// <summary>
        /// Gets or replaces a named metadata contribution; reading an absent name throws.
        /// </summary>
        /// <param name="index">
        /// The exact metadata entry name.
        /// </param>
        /// <exception cref="KeyNotFoundException">
        /// The getter cannot find the requested name.
        /// </exception>
        public GeneratorMetadataEntry this[string index] { get => this.entries[index]; set => this.entries[index] = value; }

        /// <summary>
        /// Gets or replaces constant projections, lazily creating an empty sequence when absent.
        /// </summary>
        public List<CsConstantMetadata> definedConstants { get => GetOrCreate<MetadataListEntry<CsConstantMetadata>>("DefinedConstants").values; set => this.m_entries["DefinedConstants"] = new MetadataListEntry<CsConstantMetadata>(value); }
        /// <summary>
        /// Gets or replaces enum projections, lazily creating an empty sequence when absent.
        /// </summary>
        public List<CsEnumMetadata> definedEnums { get => GetOrCreate<MetadataListEntry<CsEnumMetadata>>("DefinedEnums").values; set => this.m_entries["DefinedEnums"] = new MetadataListEntry<CsEnumMetadata>(value); }
        /// <summary>
        /// Gets or replaces managed receiver names with generated extension methods.
        /// </summary>
        public List<string> definedExtensionTypes { get => GetOrCreate<MetadataListEntry<string>>("DefinedExtensionTypes").values; set => this.m_entries["DefinedExtensionTypes"] = new MetadataListEntry<string>(value); }
        /// <summary>
        /// Gets or replaces the analyzed function descriptors projected as managed extensions.
        /// </summary>
        public List<CsFunction> definedExtensions { get => GetOrCreate<MetadataListEntry<CsFunction>>("DefinedExtensions").values; set => this.m_entries["DefinedExtensions"] = new MetadataListEntry<CsFunction>(value); }
        /// <summary>
        /// Gets or replaces native receiver names with generated COM extension methods.
        /// </summary>
        public List<string> definedCOMExtensionTypes { get => GetOrCreate<MetadataListEntry<string>>("DefinedCOMExtensionTypes").values; set => this.m_entries["DefinedCOMExtensionTypes"] = new MetadataListEntry<string>(value); }
        /// <summary>
        /// Gets or replaces managed COM variations grouped by receiver name.
        /// </summary>
        public Dictionary<string, HashSet<CsFunctionVariation>> definedCOMExtensions { get => GetOrCreate<MetadataDictionaryEntry<string, HashSet<CsFunctionVariation>>>("DefinedCOMExtensions").dictionary; set => this.m_entries["DefinedCOMExtensions"] = new MetadataDictionaryEntry<string, HashSet<CsFunctionVariation>>(value); }
        /// <summary>
        /// Gets or replaces native function identities observed during analysis.
        /// </summary>
        public List<string> cppDefinedFunctions { get => GetOrCreate<MetadataListEntry<string>>("CppDefinedFunctions").values; set => this.m_entries["CppDefinedFunctions"] = new MetadataListEntry<string>(value); }
        /// <summary>
        /// Gets or replaces analyzed native function and managed overload descriptors.
        /// </summary>
        public List<CsFunction> definedFunctions { get => GetOrCreate<MetadataListEntry<CsFunction>>("DefinedFunctions").values; set => this.m_entries["DefinedFunctions"] = new MetadataListEntry<CsFunction>(value); }
        /// <summary>
        /// Gets or replaces native alias names already emitted by this generation.
        /// </summary>
        public List<string> definedTypedefs { get => GetOrCreate<MetadataListEntry<string>>("DefinedTypedefs").values; set => this.m_entries["DefinedTypedefs"] = new MetadataListEntry<string>(value); }
        /// <summary>
        /// Gets or replaces native type names already emitted by this generation.
        /// </summary>
        public List<string> definedTypes { get => GetOrCreate<MetadataListEntry<string>>("DefinedTypes").values; set => this.m_entries["DefinedTypes"] = new MetadataListEntry<string>(value); }
        /// <summary>
        /// Gets or replaces callback delegate projections already emitted by this generation.
        /// </summary>
        public List<CsDelegate> definedDelegates { get => GetOrCreate<MetadataListEntry<CsDelegate>>("DefinedDelegates").values; set => this.m_entries["DefinedDelegates"] = new MetadataListEntry<CsDelegate>(value); }
        /// <summary>
        /// Gets or replaces native pointer carrier mappings keyed by native type name.
        /// </summary>
        public Dictionary<string, string> wrappedPointers { get => GetOrCreate<MetadataDictionaryEntry<string, string>>("WrappedPointers").dictionary; set => this.m_entries["WrappedPointers"] = new MetadataDictionaryEntry<string, string>(value); }
        /// <summary>
        /// Gets or replaces the dynamic-import entry-point table, lazily creating an empty table when absent.
        /// </summary>
        public CsFunctionTableMetadata functionTable { get => GetOrCreate<CsFunctionTableMetadata>("FunctionTable"); set => this.m_entries["FunctionTable"] = value; }

        /// <summary>
        /// Tests whether a named metadata contribution exists without creating it.
        /// </summary>
        /// <param name="key">
        /// The exact metadata entry name.
        /// </param>
        /// <returns>
        /// True when the name is present; otherwise false.
        /// </returns>
        public bool ContainsKey(string key)
        {
            return this.entries.ContainsKey(key);
        }

        /// <summary>
        /// Looks up a named contribution without creating or converting it.
        /// </summary>
        /// <param name="key">
        /// The exact entry name.
        /// </param>
        /// <param name="entry">
        /// The retained entry, or null when absent.
        /// </param>
        /// <returns>
        /// True when the named contribution exists; otherwise false.
        /// </returns>
        public bool TryGetEntry(
            string key,
            [NotNullWhen(true)] out GeneratorMetadataEntry? entry
        ) {
            return this.entries.TryGetValue(key, out entry);
        }

        /// <summary>
        /// Looks up a named contribution only when its runtime type matches the requested metadata type.
        /// </summary>
        /// <typeparam name="T">The requested metadata entry type.</typeparam>
        /// <param name="key">
        /// The exact entry name.
        /// </param>
        /// <param name="entry">
        /// The retained typed entry, or null for an absent or incompatible entry.
        /// </param>
        /// <returns>
        /// True when a compatible entry exists; otherwise false.
        /// </returns>
        public bool TryGetEntry<T>(
            string key,
            [NotNullWhen(true)] out T? entry
        )
            where T : GeneratorMetadataEntry
        {
            bool result = this.entries.TryGetValue(key, out var metadataEntry);
            if (result && metadataEntry is T t)
            {
                entry = t;
                return true;
            }

            entry = default;
            return false;
        }

        /// <summary>
        /// Looks up a typed metadata contribution without creating or replacing it.
        /// </summary>
        /// <typeparam name="T">The requested metadata entry type.</typeparam>
        /// <param name="key">
        /// The exact entry name.
        /// </param>
        /// <returns>
        /// The retained typed entry, or null for an absent or incompatible entry.
        /// </returns>
        public T? GetEntry<T>(string key)
            where T : GeneratorMetadataEntry
        {
            bool result = this.entries.TryGetValue(key, out var metadataEntry);
            if (result && metadataEntry is T t)
            {
                return t;
            }

            return default;
        }

        /// <summary>
        /// Returns a compatible entry or replaces an absent or incompatible entry with a newly constructed one.
        /// </summary>
        /// <typeparam name="T">The metadata entry type with a public parameterless constructor.</typeparam>
        /// <param name="key">
        /// The exact metadata entry name.
        /// </param>
        /// <returns>
        /// The retained compatible or newly created entry.
        /// </returns>
        public T GetOrCreate<T>(string key)
            where T : GeneratorMetadataEntry, new()
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
            this.entries[key] = entryT;
            return entryT;
        }

        /// <summary>
        /// Merges matching contributions and clones previously absent contributions from the incoming metadata.
        /// </summary>
        /// <param name="from">
        /// The source metadata retained unchanged by merging.
        /// </param>
        /// <param name="options">
        /// Options forwarded to concrete entry merge operations.
        /// </param>
        public void Merge(
            CsCodeGeneratorMetadata from,
            in MergeOptions options
        ) {
            foreach (var item in from.entries)
            {
                if (item.Value is CsFunctionTableMetadata && !options.mergeFunctionTable)
                    continue;
                if (TryGetEntry(item.Key, out var entry))
                {
                    entry.Merge(item.Value, options);
                }
                else
                {
                    entries.Add(item.Key, item.Value.Clone());
                }
            }
        }

        /// <summary>
        /// Copies the named-entry dictionary while either retaining entries or invoking their individual cloning policies.
        /// </summary>
        /// <param name="shallow">
        /// True to retain the entry objects; false to clone each entry.
        /// </param>
        /// <returns>
        /// A new metadata container retaining the same configuration object.
        /// </returns>
        public CsCodeGeneratorMetadata Clone(bool shallow = false)
        {
            CsCodeGeneratorMetadata metadata = new();
            metadata.settings = this.settings;
            foreach (var item in this.entries)
            {
                if (shallow)
                {
                    metadata.entries[item.Key] = item.Value;
                }
                else
                {
                    metadata.entries[item.Key] = item.Value.Clone();
                }
            }

            return metadata;
        }

    }
}
