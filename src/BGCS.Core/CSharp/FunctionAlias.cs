namespace BGCS.Core.CSharp
{
    /// <summary>
    /// Defines the public class <c>FunctionAlias</c>.
    /// </summary>
    public class FunctionAlias
    {
        /// <summary>
        /// Initializes a new instance of <see cref="FunctionAlias"/>.
        /// </summary>
        public FunctionAlias(string exportedName, string exportedAliasName, string friendlyName, string? comment)
        {
            ExportedName = exportedName;
            ExportedAliasName = exportedAliasName;
            FriendlyName = friendlyName;
            Comment = comment;
        }

        /// <summary>
        /// Gets or sets <c>ExportedName</c>.
        /// </summary>
        public string ExportedName { get; set; }

        /// <summary>
        /// Gets or sets <c>ExportedAliasName</c>.
        /// </summary>
        public string ExportedAliasName { get; set; }

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
