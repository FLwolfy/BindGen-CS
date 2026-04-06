namespace BGCS.Core.Mapping
{
    /// <summary>
    /// Defines the public class <c>ConstantMapping</c>.
    /// </summary>
    public class ConstantMapping
    {
        /// <summary>
        /// Initializes a new instance of <see cref="ConstantMapping"/>.
        /// </summary>
        public ConstantMapping(string exportedName, string friendlyName, string comment, string type, string value)
        {
            ExportedName = exportedName;
            FriendlyName = friendlyName;
            Comment = comment;
            Type = type;
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
        /// Gets or sets <c>Type</c>.
        /// </summary>
        public string? Type { get; set; }

        /// <summary>
        /// Gets or sets <c>Value</c>.
        /// </summary>
        public string? Value { get; set; }
    }
}
