namespace BGCS.Configuration.Mapping
{
    /// <summary>
    /// Overrides the managed name, expression, or documentation of an exact native enumeration item.
    /// </summary>
    public class EnumItemMapping
    {
        /// <summary>
        /// Captures optional managed overrides for one native enumeration item.
        /// </summary>
        /// <param name="exportedName">
        /// The exact native item identifier to select.
        /// </param>
        /// <param name="friendlyName">
        /// The managed item identifier override, or null to retain naming policy.
        /// </param>
        /// <param name="comment">
        /// The documentation override, or null when absent.
        /// </param>
        /// <param name="value">
        /// The managed constant-expression override, or null to retain the analyzed value.
        /// </param>
        public EnumItemMapping(
            string exportedName,
            string? friendlyName,
            string? comment,
            string? value
        ) {
            this.exportedName = exportedName;
            this.friendlyName = friendlyName;
            this.comment = comment;
            this.value = value;
        }

        /// <summary>
        /// Gets or sets the exact native enum-item identifier selected by this rule.
        /// </summary>
        public string exportedName { get; set; }
        /// <summary>
        /// Gets or sets the managed item name override, or null to retain naming policy.
        /// </summary>
        public string? friendlyName { get; set; }
        /// <summary>
        /// Gets or sets the item documentation override, or null when absent.
        /// </summary>
        public string? comment { get; set; }
        /// <summary>
        /// Gets or sets the managed constant expression override, or null to retain the analyzed expression.
        /// </summary>
        public string? value { get; set; }
    }
}
