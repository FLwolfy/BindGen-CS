namespace BGCS.Core.Mapping
{
    /// <summary>
    /// Describes a boolean validity property backed by a native sentinel-valued struct field.
    /// </summary>
    public sealed class StructValidityMapping
    {
        /// <summary>
        /// Initializes a new validity mapping.
        /// </summary>
        /// <param name="fieldName">The native field whose value is compared with the sentinel.</param>
        /// <param name="invalidValue">A C# constant expression representing the invalid sentinel value.</param>
        /// <param name="propertyName">The generated property name.</param>
        public StructValidityMapping(string fieldName, string invalidValue, string propertyName = "IsValid")
        {
            FieldName = fieldName;
            InvalidValue = invalidValue;
            PropertyName = propertyName;
        }

        /// <summary>
        /// Gets or sets the native field whose value is compared with the sentinel.
        /// </summary>
        public string FieldName { get; set; }

        /// <summary>
        /// Gets or sets a C# constant expression representing the invalid sentinel value.
        /// </summary>
        public string InvalidValue { get; set; }

        /// <summary>
        /// Gets or sets the generated boolean property name.
        /// </summary>
        public string PropertyName { get; set; }
    }
}
