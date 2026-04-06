namespace BGCS.Metadata
{
    using BGCS;
    using BGCS.Core;
    using Newtonsoft.Json;

    /// <summary>
    /// Defines the public class <c>CsConstantMetadata</c>.
    /// </summary>
    public class CsConstantMetadata : IHasIdentifier, ICloneable<CsConstantMetadata>
    {
        /// <summary>
        /// Initializes a new instance of <see cref="CsConstantMetadata"/>.
        /// </summary>
        public CsConstantMetadata(string cppName, string cppValue, CsConstantType type)
        {
            CppName = cppName;
            CppValue = cppValue;
            Type = type;
        }

        [JsonConstructor]
        /// <summary>
        /// Executes public operation <c>CsConstantMetadata</c>.
        /// </summary>
        public CsConstantMetadata(string cppName, string cppValue, string? name, string? value, CsConstantType type, string? comment)
        {
            CppName = cppName;
            CppValue = cppValue;
            Name = name;
            Value = value;
            Type = type;
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
        /// Executes public operation <c>ToLiteral</c>.
        /// </summary>
        public string EscapedCppValue => CppValue.ToLiteral();

        /// <summary>
        /// Gets or sets <c>Name</c>.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Gets or sets <c>Value</c>.
        /// </summary>
        public string? Value { get; set; }

        /// <summary>
        /// Gets or sets <c>Type</c>.
        /// </summary>
        public CsConstantType Type { get; set; }

        /// <summary>
        /// Gets or sets <c>CustomType</c>.
        /// </summary>
        public string? CustomType { get; set; }

        /// <summary>
        /// Gets or sets <c>Comment</c>.
        /// </summary>
        public string? Comment { get; set; }

        /// <summary>
        /// Returns computed data from <c>GetHashCode</c>.
        /// </summary>
        public override int GetHashCode()
        {
            return CppName.GetHashCode();
        }

        /// <summary>
        /// Executes public operation <c>ToString</c>.
        /// </summary>
        public override string ToString()
        {
            return $"Constant: {CppName} = {CppValue}";
        }

        /// <summary>
        /// Executes public operation <c>Clone</c>.
        /// </summary>
        public CsConstantMetadata Clone()
        {
            return new CsConstantMetadata(CppName, CppValue, Name, Value, Type, Comment);
        }
    }
}
