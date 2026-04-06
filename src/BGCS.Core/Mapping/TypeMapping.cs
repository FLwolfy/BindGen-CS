namespace BGCS.Core.Mapping
{
    using System.Diagnostics.CodeAnalysis;

    /// <summary>
    /// Defines the public class <c>TypeMapping</c>.
    /// </summary>
    public class TypeMapping
    {
        /// <summary>
        /// Initializes a new instance of <see cref="TypeMapping"/>.
        /// </summary>
        public TypeMapping(string exportedName, string? friendlyName, string? comment)
        {
            ExportedName = exportedName;
            FriendlyName = friendlyName;
            Comment = comment;
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
        /// Gets or sets <c>FieldMappings</c>.
        /// </summary>
        public List<TypeFieldMapping> FieldMappings { get; set; } = new();

        /// <summary>
        /// Attempts to resolve data via <c>TryGetFieldMapping</c> without throwing.
        /// </summary>
        public bool TryGetFieldMapping(string valueName, [NotNullWhen(true)] out TypeFieldMapping? mapping)
        {
            for (int i = 0; i < FieldMappings.Count; i++)
            {
                var fieldMapping = FieldMappings[i];
                if (fieldMapping.ExportedName == valueName)
                {
                    mapping = fieldMapping;
                    return true;
                }
            }

            mapping = null;
            return false;
        }

        /// <summary>
        /// Returns computed data from <c>GetFieldMapping</c>.
        /// </summary>
        public TypeFieldMapping? GetFieldMapping(string valueName)
        {
            for (int i = 0; i < FieldMappings.Count; i++)
            {
                var fieldMapping = FieldMappings[i];
                if (fieldMapping.ExportedName == valueName)
                {
                    return fieldMapping;
                }
            }

            return null;
        }
    }
}
