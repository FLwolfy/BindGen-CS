namespace BGCS.Core.Mapping
{
    /// <summary>
    /// Defines the public class <c>HandleMapping</c>.
    /// </summary>
    public class HandleMapping
    {
        /// <summary>
        /// Initializes a new instance of <see cref="HandleMapping"/>.
        /// </summary>
        public HandleMapping(string exportedName, string? friendlyName, string? comment)
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
    }
}
