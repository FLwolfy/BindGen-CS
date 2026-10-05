using System.Collections.Generic;

namespace BGCS.Configuration.Mapping
{
    using System.Diagnostics.CodeAnalysis;

    /// <summary>
    /// Overrides one native record's managed name, documentation, field mappings, and optional validity projection.
    /// </summary>
    public class TypeMapping
    {
        /// <summary>
        /// Retains native record identity and managed naming and documentation overrides.
        /// </summary>
        /// <param name="exportedName">
        /// The exact native record identifier.
        /// </param>
        /// <param name="friendlyName">
        /// The managed record identifier override, or null to use naming rules.
        /// </param>
        /// <param name="comment">
        /// The documentation override, or null to retain source-derived documentation.
        /// </param>
        public TypeMapping(
            string exportedName,
            string? friendlyName,
            string? comment
        ) {
            this.exportedName = exportedName;
            this.friendlyName = friendlyName;
            this.comment = comment;
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
        /// Gets or sets explicit native-field projections in matching priority order.
        /// </summary>
        public List<TypeFieldMapping> fieldMappings { get; set; } = new();
        /// <summary>
        /// Gets or sets an optional sentinel-based validity property for this value type.
        /// </summary>
        public StructValidityMapping? validity { get; set; }

        /// <summary>
        /// Finds the first field mapping whose native identifier exactly matches the requested spelling.
        /// </summary>
        /// <param name="valueName">
        /// The native field identifier matched with ordinal equality.
        /// </param>
        /// <param name="mapping">
        /// The retained matching mapping, or null when absent.
        /// </param>
        /// <returns>
        /// True when a matching mapping exists; otherwise false.
        /// </returns>
        public bool TryGetFieldMapping(
            string valueName,
            [NotNullWhen(true)] out TypeFieldMapping? mapping
        ) {
            for (int i = 0; i < this.fieldMappings.Count; i++)
            {
                var fieldMapping = this.fieldMappings[i];
                if (fieldMapping.exportedName == valueName)
                {
                    mapping = fieldMapping;
                    return true;
                }
            }

            mapping = null;
            return false;
        }

        /// <summary>
        /// Finds the first field mapping whose native identifier exactly matches the requested spelling.
        /// </summary>
        /// <param name="valueName">
        /// The native field identifier matched with ordinal equality.
        /// </param>
        /// <returns>
        /// The retained matching mapping, or null when absent.
        /// </returns>
        public TypeFieldMapping? GetFieldMapping(string valueName)
        {
            for (int i = 0; i < this.fieldMappings.Count; i++)
            {
                var fieldMapping = this.fieldMappings[i];
                if (fieldMapping.exportedName == valueName)
                {
                    return fieldMapping;
                }
            }

            return null;
        }
    }
}
