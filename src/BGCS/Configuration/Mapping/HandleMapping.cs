namespace BGCS.Configuration.Mapping
{
    /// <summary>
    /// Overrides the managed presentation of an exact native opaque handle identifier.
    /// </summary>
    public class HandleMapping
    {
        /// <summary>
        /// Captures the native selector and its optional managed projection overrides.
        /// </summary>
        /// <param name="exportedName">
        /// The exact native handle identifier selected by this rule.
        /// </param>
        /// <param name="friendlyName">
        /// The managed handle identifier override, or null to retain naming policy.
        /// </param>
        /// <param name="comment">
        /// The handle documentation override, or null when absent.
        /// </param>
        public HandleMapping(
            string exportedName,
            string? friendlyName,
            string? comment
        ) {
            this.exportedName = exportedName;
            this.friendlyName = friendlyName;
            this.comment = comment;
        }

        /// <summary>
        /// Gets or sets the exact native handle identifier selected by this rule.
        /// </summary>
        public string exportedName { get; set; }
        /// <summary>
        /// Gets or sets the managed handle identifier override, or null to retain naming policy.
        /// </summary>
        public string? friendlyName { get; set; }
        /// <summary>
        /// Gets or sets optional handle documentation, or null when absent.
        /// </summary>
        public string? comment { get; set; }
    }
}
