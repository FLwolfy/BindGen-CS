namespace BGCS.Metadata
{
    using BGCS.Core;
    using Newtonsoft.Json;

    /// <summary>
    /// Defines the public class <c>CsEnumMetadata</c>.
    /// </summary>
    public class CsEnumMetadata : IHasIdentifier, ICloneable<CsEnumMetadata>
    {
        [JsonConstructor]
        /// <summary>
        /// Initializes a new instance of <see cref="CsEnumMetadata"/>.
        /// </summary>
        public CsEnumMetadata(string cppName, string name, List<string> attributes, string? comment, string baseType, List<CsEnumItemMetadata> items)
        {
            CppName = cppName;
            Name = name;
            Attributes = attributes;
            Comment = comment;
            Items = items;
            BaseType = baseType;
            Items = items;
        }

        /// <summary>
        /// Executes public operation <c>CsEnumMetadata</c>.
        /// </summary>
        public CsEnumMetadata(string cppName, string name, List<string> attributes, string? comment, List<CsEnumItemMetadata> items)
        {
            CppName = cppName;
            Name = name;
            Attributes = attributes;
            Comment = comment;
            Items = items;
            BaseType = "int";
        }

        /// <summary>
        /// Executes public operation <c>CsEnumMetadata</c>.
        /// </summary>
        public CsEnumMetadata(string cppName, string name, List<string> attributes, string? comment)
        {
            CppName = cppName;
            Name = name;
            Attributes = attributes;
            Comment = comment;
            Items = new();
            BaseType = "int";
        }

        /// <summary>
        /// Exposes public member <c>CppName</c>.
        /// </summary>
        public string Identifier => CppName;

        /// <summary>
        /// Gets or sets <c>CppName</c>.
        /// </summary>
        public string CppName { get; set; }

        /// <summary>
        /// Gets or sets <c>Name</c>.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets <c>Attributes</c>.
        /// </summary>
        public List<string> Attributes { get; set; }

        /// <summary>
        /// Gets or sets <c>Comment</c>.
        /// </summary>
        public string? Comment { get; set; }

        /// <summary>
        /// Gets or sets <c>BaseType</c>.
        /// </summary>
        public string BaseType { get; set; }

        /// <summary>
        /// Gets or sets <c>Items</c>.
        /// </summary>
        public List<CsEnumItemMetadata> Items { get; set; } = new();

        /// <summary>
        /// Returns computed data from <c>GetHashCode</c>.
        /// </summary>
        public override int GetHashCode()
        {
            return Identifier.GetHashCode();
        }

        /// <summary>
        /// Executes public operation <c>Clone</c>.
        /// </summary>
        public CsEnumMetadata Clone()
        {
            return new CsEnumMetadata(CppName, Name, new List<string>(Attributes), Comment, BaseType, Items.Select(item => item.Clone()).ToList()
            );
        }
    }
}
