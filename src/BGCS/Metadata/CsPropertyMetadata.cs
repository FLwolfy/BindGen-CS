namespace BGCS.Metadata
{
    using BGCS.Core.CSharp;
    using BGCS.CppAst.Model.Types;

    /// <summary>
    /// Defines the public class <c>CsPropertyMetadata</c>.
    /// </summary>
    public class CsPropertyMetadata
    {
        /// <summary>
        /// Initializes a new instance of <see cref="CsPropertyMetadata"/>.
        /// </summary>
        public CsPropertyMetadata(CppType cppType, CsType type, string name, string getter, string setter, string? comment = null, List<string>? attributes = null)
        {
            CppType = cppType;
            Type = type;
            Name = name;
            Comment = comment;
            Getter = getter;
            Setter = setter;
            Attributes = attributes;
        }

        /// <summary>
        /// Gets or sets <c>CppType</c>.
        /// </summary>
        public CppType CppType { get; set; }

        /// <summary>
        /// Gets or sets <c>Type</c>.
        /// </summary>
        public CsType Type { get; set; }

        /// <summary>
        /// Gets or sets <c>Name</c>.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets <c>Comment</c>.
        /// </summary>
        public string? Comment { get; set; }

        /// <summary>
        /// Gets or sets <c>Getter</c>.
        /// </summary>
        public string Getter { get; set; }

        /// <summary>
        /// Gets or sets <c>Setter</c>.
        /// </summary>
        public string Setter { get; set; }

        /// <summary>
        /// Gets or sets <c>Attributes</c>.
        /// </summary>
        public List<string>? Attributes { get; set; }
    }
}
