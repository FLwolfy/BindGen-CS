namespace BGCS.Configuration.Mapping
{
    /// <summary>
    /// Overrides a managed callback signature supplied for an exact delegate identifier.
    /// </summary>
    public class DelegateMapping
    {
        /// <summary>
        /// Captures the native selector and its optional managed projection overrides.
        /// </summary>
        /// <param name="name">
        /// The delegate identifier selected by this mapping.
        /// </param>
        /// <param name="returnType">
        /// The managed return-type spelling.
        /// </param>
        /// <param name="signature">
        /// The managed parameter signature text.
        /// </param>
        public DelegateMapping(
            string name,
            string returnType,
            string signature
        ) {
            this.name = name;
            this.returnType = returnType;
            this.signature = signature;
        }

        /// <summary>
        /// Gets or sets the delegate identifier selected by this rule.
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// Gets or sets the managed callback return-type spelling.
        /// </summary>
        public string returnType { get; set; }
        /// <summary>
        /// Gets or sets the managed callback parameter-signature text.
        /// </summary>
        public string signature { get; set; }
    }
}
