using System.Collections.Generic;

namespace BGCS.Configuration.Mapping
{
    using System.Diagnostics.CodeAnalysis;
    using Newtonsoft.Json;

    /// <summary>
    /// Overrides one native enum's managed name, documentation, and item projections.
    /// </summary>
    public class EnumMapping
    {
        /// <summary>
        /// Retains native enum identity and initializes an empty item-mapping sequence.
        /// </summary>
        /// <param name="exportedName">
        /// The exact native enum identifier.
        /// </param>
        /// <param name="friendlyName">
        /// The managed enum identifier override, or null to use naming rules.
        /// </param>
        /// <param name="comment">
        /// The documentation override, or null to retain source-derived documentation.
        /// </param>
        public EnumMapping(
            string exportedName,
            string? friendlyName,
            string? comment
        ) {
            this.exportedName = exportedName;
            this.friendlyName = friendlyName;
            this.comment = comment;
            this.itemMappings = new();
        }

        /// <summary>
        /// Retains enum identity and caller-owned item-mapping containers without copying.
        /// </summary>
        /// <param name="exportedName">
        /// The exact native enum identifier.
        /// </param>
        /// <param name="friendlyName">
        /// The managed enum identifier override, or null to use naming rules.
        /// </param>
        /// <param name="comment">
        /// The documentation override, or null to retain source-derived documentation.
        /// </param>
        /// <param name="itemMappings">
        /// The ordered mutable item mappings retained by this configuration.
        /// </param>
        [JsonConstructor]
        public EnumMapping(
            string exportedName,
            string? friendlyName,
            string? comment,
            List<EnumItemMapping> itemMappings
        ) {
            this.exportedName = exportedName;
            this.friendlyName = friendlyName;
            this.comment = comment;
            this.itemMappings = itemMappings;
        }

        /// <summary>
        /// Gets or sets the exact native identifier used to select this mapping.
        /// </summary>
        public string exportedName { get; set; }
        /// <summary>
        /// Gets or sets the managed identifier override, or null to use configured naming rules.
        /// </summary>
        public string? friendlyName { get; set; }
        /// <summary>
        /// Gets or sets emitted documentation override text, or null to retain source-derived documentation.
        /// </summary>
        public string? comment { get; set; }
        /// <summary>
        /// Gets or sets explicit native enum-item projections in matching priority order.
        /// </summary>
        public List<EnumItemMapping> itemMappings { get; set; }

        /// <summary>
        /// Finds the first item mapping whose native identifier exactly matches the requested spelling.
        /// </summary>
        /// <param name="valueName">
        /// The native enum-item identifier matched with ordinal equality.
        /// </param>
        /// <param name="mapping">
        /// The retained matching mapping, or null when absent.
        /// </param>
        /// <returns>
        /// True when a matching mapping exists; otherwise false.
        /// </returns>
        public bool TryGetItemMapping(
            string valueName,
            [NotNullWhen(true)] out EnumItemMapping? mapping
        ) {
            for (int i = 0; i < this.itemMappings.Count; i++)
            {
                var enumItemMapping = this.itemMappings[i];
                if (enumItemMapping.exportedName == valueName)
                {
                    mapping = enumItemMapping;
                    return true;
                }
            }

            mapping = null;
            return false;
        }

        /// <summary>
        /// Finds the first item mapping whose native identifier exactly matches the requested spelling.
        /// </summary>
        /// <param name="valueName">
        /// The native enum-item identifier matched with ordinal equality.
        /// </param>
        /// <returns>
        /// The retained matching mapping, or null when absent.
        /// </returns>
        public EnumItemMapping? GetItemMapping(string valueName)
        {
            for (int i = 0; i < this.itemMappings.Count; i++)
            {
                var enumItemMapping = this.itemMappings[i];
                if (enumItemMapping.exportedName == valueName)
                {
                    return enumItemMapping;
                }
            }

            return null;
        }
    }
}
