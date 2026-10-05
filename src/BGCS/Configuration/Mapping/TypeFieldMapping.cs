namespace BGCS.Configuration.Mapping
{
    /// <summary>
    /// Overrides the managed presentation of an exact native record field identifier.
    /// </summary>
    public class TypeFieldMapping
    {
        /// <summary>
        /// Captures the native selector and its optional managed projection overrides.
        /// </summary>
        /// <param name="exportedName">
        /// The exact native field identifier selected by this rule.
        /// </param>
        /// <param name="displayName">
        /// The managed field identifier override, or null to retain naming policy.
        /// </param>
        /// <param name="comment">
        /// The field documentation override, or null when absent.
        /// </param>
        public TypeFieldMapping(
            string exportedName,
            string? displayName,
            string? comment
        ) {
            this.exportedName = exportedName;
            this.displayName = displayName;
            this.comment = comment;
        }

        /// <summary>
        /// Gets or sets the exact native field identifier selected by this rule.
        /// </summary>
        public string exportedName { get; set; }
        /// <summary>
        /// Gets or sets the managed field identifier override, or null to retain naming policy.
        /// </summary>
        public string? displayName { get; set; }
        /// <summary>
        /// Gets or sets optional field documentation, or null when absent.
        /// </summary>
        public string? comment { get; set; }
    }
}
