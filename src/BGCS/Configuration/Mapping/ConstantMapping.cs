namespace BGCS.Configuration.Mapping
{
    /// <summary>
    /// Overrides one native constant's managed identifier, carrier spelling, expression, and documentation.
    /// </summary>
    public class ConstantMapping
    {
        /// <summary>
        /// Retains explicit managed projection overrides for a native constant.
        /// </summary>
        /// <param name="exportedName">
        /// The exact native constant identifier.
        /// </param>
        /// <param name="friendlyName">
        /// The managed identifier override.
        /// </param>
        /// <param name="comment">
        /// The emitted documentation override.
        /// </param>
        /// <param name="type">
        /// The managed carrier spelling override.
        /// </param>
        /// <param name="value">
        /// The managed expression spelling override.
        /// </param>
        public ConstantMapping(
            string exportedName,
            string friendlyName,
            string comment,
            string type,
            string value
        ) {
            this.exportedName = exportedName;
            this.friendlyName = friendlyName;
            this.comment = comment;
            this.type = type;
            this.value = value;
        }

        /// <summary>
        /// Gets or sets the exact native identifier used to select this mapping.
        /// </summary>
        public string exportedName { get; set; }
        /// <summary>
        /// Gets or sets the managed identifier override, or null to use configured naming rules.
        /// </summary>
        public string? friendlyName { get; set; }
        /// <summary>
        /// Gets or sets emitted documentation override text, or null to retain source-derived documentation.
        /// </summary>
        public string? comment { get; set; }
        /// <summary>
        /// Gets or sets the managed carrier spelling override, or null to infer a carrier from the native constant.
        /// </summary>
        public string? type { get; set; }
        /// <summary>
        /// Gets or sets the managed expression spelling override, or null to project the native expression.
        /// </summary>
        public string? value { get; set; }
    }
}
