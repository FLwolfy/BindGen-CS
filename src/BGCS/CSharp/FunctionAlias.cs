namespace BGCS.CSharp
{
    /// <summary>
    /// Retains one mutable managed alias of an original native function export during analysis.
    /// </summary>
    public class FunctionAlias
    {
        /// <summary>
        /// Captures an alias without changing the underlying native export.
        /// </summary>
        /// <param name="exportedName">
        /// The original function symbol used for the native call.
        /// </param>
        /// <param name="exportedAliasName">
        /// The source macro or configured alias identifier.
        /// </param>
        /// <param name="friendlyName">
        /// The managed wrapper name selected for the alias.
        /// </param>
        /// <param name="comment">
        /// The optional alias documentation fragment, or null when absent.
        /// </param>
        public FunctionAlias(
            string exportedName,
            string exportedAliasName,
            string friendlyName,
            string? comment
        ) {
            this.exportedName = exportedName;
            this.exportedAliasName = exportedAliasName;
            this.friendlyName = friendlyName;
            this.comment = comment;
        }

        /// <summary>
        /// Gets or sets the original native function symbol invoked by this alias.
        /// </summary>
        public string exportedName { get; set; }
        /// <summary>
        /// Gets or sets the source macro or configured alias identifier.
        /// </summary>
        public string exportedAliasName { get; set; }
        /// <summary>
        /// Gets or sets the managed wrapper identifier for this alias.
        /// </summary>
        public string friendlyName { get; set; }
        /// <summary>
        /// Gets or sets optional alias documentation, or null when absent.
        /// </summary>
        public string? comment { get; set; }
    }
}
