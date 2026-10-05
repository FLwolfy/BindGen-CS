namespace BGCS.Configuration.Mapping
{
    /// <summary>
    /// Selects a managed alias presentation for one original native function symbol.
    /// </summary>
    public class FunctionAliasMapping
    {
        /// <summary>
        /// Captures an alias naming rule retained by the configuration.
        /// </summary>
        /// <param name="exportedName">
        /// The original function symbol used for the native call.
        /// </param>
        /// <param name="exportedAliasName">
        /// The source macro or configured alias identifier.
        /// </param>
        /// <param name="friendlyName">
        /// The managed alias identifier, or null to derive it from the source alias name.
        /// </param>
        /// <param name="comment">
        /// The optional alias documentation fragment, or null when absent.
        /// </param>
        public FunctionAliasMapping(
            string exportedName,
            string exportedAliasName,
            string? friendlyName,
            string? comment
        ) {
            this.exportedName = exportedName;
            this.exportedAliasName = exportedAliasName;
            this.friendlyName = friendlyName;
            this.comment = comment;
        }

        /// <summary>
        /// Gets or sets the original native symbol invoked by this alias.
        /// </summary>
        public string exportedName { get; set; }
        /// <summary>
        /// Gets or sets the exact source alias identifier selected by this rule.
        /// </summary>
        public string exportedAliasName { get; set; }
        /// <summary>
        /// Gets or sets the managed alias identifier, or null to derive it from naming policy.
        /// </summary>
        public string? friendlyName { get; set; }
        /// <summary>
        /// Gets or sets the optional alias documentation fragment, or null when absent.
        /// </summary>
        public string? comment { get; set; }
    }
}
