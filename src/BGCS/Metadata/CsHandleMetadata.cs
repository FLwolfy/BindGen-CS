namespace BGCS.Metadata
{
    using BGCS.CppAst.Model.Declarations;
    using Newtonsoft.Json;
    using System.Xml.Serialization;

    /// <summary>
    /// Defines the public class <c>CsHandleMetadata</c>.
    /// </summary>
    public class CsHandleMetadata
    {
        /// <summary>
        /// Initializes a new instance of <see cref="CsHandleMetadata"/>.
        /// </summary>
        public CsHandleMetadata(string name, CppTypedef cppType, string? comment, bool isDispatchable)
        {
            Name = name;
            CppType = cppType;
            Comment = comment;
            IsDispatchable = isDispatchable;
        }

        /// <summary>
        /// Gets or sets <c>Name</c>.
        /// </summary>
        public string Name { get; set; }

        [XmlIgnore]
        [JsonIgnore]
        /// <summary>
        /// Gets or sets <c>CppType</c>.
        /// </summary>
        public CppTypedef CppType { get; set; }

        /// <summary>
        /// Gets or sets <c>Comment</c>.
        /// </summary>
        public string? Comment { get; set; }

        /// <summary>
        /// Gets or sets <c>IsDispatchable</c>.
        /// </summary>
        public bool IsDispatchable { get; set; }
    }
}
