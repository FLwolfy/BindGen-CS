namespace BGCS.Core.Mapping
{
    /// <summary>
    /// Defines the public class <c>DelegateMapping</c>.
    /// </summary>
    public class DelegateMapping
    {
        /// <summary>
        /// Initializes a new instance of <see cref="DelegateMapping"/>.
        /// </summary>
        public DelegateMapping(string name, string returnType, string signature)
        {
            Name = name;
            ReturnType = returnType;
            Signature = signature;
        }

        /// <summary>
        /// Gets or sets <c>Name</c>.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets <c>ReturnType</c>.
        /// </summary>
        public string ReturnType { get; set; }

        /// <summary>
        /// Gets or sets <c>Signature</c>.
        /// </summary>
        public string Signature { get; set; }
    }
}
