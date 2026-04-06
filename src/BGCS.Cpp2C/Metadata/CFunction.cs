namespace BGCS.Cpp2C.Metadata
{
    /// <summary>
    /// Defines the public class <c>CFunction</c>.
    /// </summary>
    public class CFunction
    {
        /// <summary>
        /// Initializes a new instance of <see cref="CFunction"/>.
        /// </summary>
        public CFunction(string exportedName, string friendlyName, string? comment)
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
        public string FriendlyName { get; set; }

        /// <summary>
        /// Gets or sets <c>Comment</c>.
        /// </summary>
        public string? Comment { get; set; }
    }
}
