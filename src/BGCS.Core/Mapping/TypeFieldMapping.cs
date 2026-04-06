namespace BGCS.Core.Mapping
{
    /// <summary>
    /// Defines the public class <c>TypeFieldMapping</c>.
    /// </summary>
    public class TypeFieldMapping
    {
        /// <summary>
        /// Initializes a new instance of <see cref="TypeFieldMapping"/>.
        /// </summary>
        public TypeFieldMapping(string exportedName, string? displayName, string? comment)
        {
            ExportedName = exportedName;
            DisplayName = displayName;
            Comment = comment;
        }

        /// <summary>
        /// Gets or sets <c>ExportedName</c>.
        /// </summary>
        public string ExportedName { get; set; }

        /// <summary>
        /// Gets or sets <c>DisplayName</c>.
        /// </summary>
        public string? DisplayName { get; set; }

        /// <summary>
        /// Gets or sets <c>Comment</c>.
        /// </summary>
        public string? Comment { get; set; }
    }
}
