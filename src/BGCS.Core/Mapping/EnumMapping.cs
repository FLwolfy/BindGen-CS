namespace BGCS.Core.Mapping
{
    using System.Diagnostics.CodeAnalysis;
    using Newtonsoft.Json;

    /// <summary>
    /// Defines the public class <c>EnumMapping</c>.
    /// </summary>
    public class EnumMapping
    {
        /// <summary>
        /// Initializes a new instance of <see cref="EnumMapping"/>.
        /// </summary>
        public EnumMapping(string exportedName, string? friendlyName, string? comment)
        {
            ExportedName = exportedName;
            FriendlyName = friendlyName;
            Comment = comment;
            ItemMappings = new();
        }

        [JsonConstructor]
        /// <summary>
        /// Executes public operation <c>EnumMapping</c>.
        /// </summary>
        public EnumMapping(string exportedName, string? friendlyName, string? comment, List<EnumItemMapping> itemMappings)
        {
            ExportedName = exportedName;
            FriendlyName = friendlyName;
            Comment = comment;
            ItemMappings = itemMappings;
        }

        /// <summary>
        /// Gets or sets <c>ExportedName</c>.
        /// </summary>
        public string ExportedName { get; set; }

        /// <summary>
        /// Gets or sets <c>FriendlyName</c>.
        /// </summary>
        public string? FriendlyName { get; set; }

        /// <summary>
        /// Gets or sets <c>Comment</c>.
        /// </summary>
        public string? Comment { get; set; }

        /// <summary>
        /// Gets or sets <c>ItemMappings</c>.
        /// </summary>
        public List<EnumItemMapping> ItemMappings { get; set; }

        /// <summary>
        /// Attempts to resolve data via <c>TryGetItemMapping</c> without throwing.
        /// </summary>
        public bool TryGetItemMapping(string valueName, [NotNullWhen(true)] out EnumItemMapping? mapping)
        {
            for (int i = 0; i < ItemMappings.Count; i++)
            {
                var enumItemMapping = ItemMappings[i];
                if (enumItemMapping.ExportedName == valueName)
                {
                    mapping = enumItemMapping;
                    return true;
                }
            }

            mapping = null;
            return false;
        }

        /// <summary>
        /// Returns computed data from <c>GetItemMapping</c>.
        /// </summary>
        public EnumItemMapping? GetItemMapping(string valueName)
        {
            for (int i = 0; i < ItemMappings.Count; i++)
            {
                var enumItemMapping = ItemMappings[i];
                if (enumItemMapping.ExportedName == valueName)
                {
                    return enumItemMapping;
                }
            }

            return null;
        }
    }
}
