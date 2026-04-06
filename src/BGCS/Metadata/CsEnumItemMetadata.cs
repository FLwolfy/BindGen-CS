namespace BGCS.Metadata
{
    using BGCS.Core;
    using Newtonsoft.Json;

    /// <summary>
    /// Defines the public class <c>CsEnumItemMetadata</c>.
    /// </summary>
    public class CsEnumItemMetadata : IHasIdentifier
    {
        /// <summary>
        /// Initializes a new instance of <see cref="CsEnumItemMetadata"/>.
        /// </summary>
        public CsEnumItemMetadata(string cppName, string cppValue)
        {
            CppName = cppName;
            CppValue = cppValue;
            Attributes = new();
        }

        [JsonConstructor]
        /// <summary>
        /// Executes public operation <c>CsEnumItemMetadata</c>.
        /// </summary>
        public CsEnumItemMetadata(string cppName, string cppValue, string? name, string? value, List<string> attributes, string? comment)
        {
            CppName = cppName;
            CppValue = cppValue;
            Name = name;
            Value = value;
            Attributes = attributes;
            Comment = comment;
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
        /// Gets or sets <c>CppValue</c>.
        /// </summary>
        public string CppValue { get; set; }

        /// <summary>
        /// Gets or sets <c>Name</c>.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Gets or sets <c>Value</c>.
        /// </summary>
        public string? Value { get; set; }

        /// <summary>
        /// Gets or sets <c>Attributes</c>.
        /// </summary>
        public List<string> Attributes { get; set; }

        /// <summary>
        /// Gets or sets <c>Comment</c>.
        /// </summary>
        public string? Comment { get; set; }

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
        public CsEnumItemMetadata Clone()
        {
            return new CsEnumItemMetadata(CppName, CppValue, Name, Value, new List<string>(Attributes), Comment);
        }
    }
}
