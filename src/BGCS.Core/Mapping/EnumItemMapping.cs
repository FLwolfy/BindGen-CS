namespace BGCS.Core.Mapping
{
    /// <summary>
    /// Defines the public class <c>EnumItemMapping</c>.
    /// </summary>
    public class EnumItemMapping
    {
        /// <summary>
        /// Initializes a new instance of <see cref="EnumItemMapping"/>.
        /// </summary>
        public EnumItemMapping(string exportedName, string? friendlyName, string? comment, string? value)
        {
            ExportedName = exportedName;
            FriendlyName = friendlyName;
            Comment = comment;
            Value = value;
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
        /// Gets or sets <c>Value</c>.
        /// </summary>
        public string? Value { get; set; }
    }
}
