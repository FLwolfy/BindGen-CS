namespace BGCS.Core.CSharp
{
    using BGCS.Core;
    using System.Collections.Generic;
    using Newtonsoft.Json;

    /// <summary>
    /// Defines the public class <c>CsDelegate</c>.
    /// </summary>
    public class CsDelegate : IHasIdentifier, ICloneable<CsDelegate>
    {
        [JsonConstructor]
        /// <summary>
        /// Initializes a new instance of <see cref="CsDelegate"/>.
        /// </summary>
        public CsDelegate(string cppName, string name, CsType returnType, List<CsParameterInfo> parameters, List<string>? attributes = null, string? comment = null)
        {
            CppName = cppName;
            Name = name;
            ReturnType = returnType;
            Parameters = parameters;
            Attributes = attributes ?? [];
            Comment = comment;
        }

        /// <summary>
        /// Executes public operation <c>CsDelegate</c>.
        /// </summary>
        public CsDelegate(string cppName, string name, CsType returnType, List<CsParameterInfo> parameters)
        {
            CppName = cppName;
            Name = name;
            ReturnType = returnType;
            Parameters = parameters;
            Attributes = [];
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
        /// Gets or sets <c>ReturnType</c>.
        /// </summary>
        public CsType ReturnType { get; set; }

        /// <summary>
        /// Gets or sets <c>Parameters</c>.
        /// </summary>
        public List<CsParameterInfo> Parameters { get; set; }

        /// <summary>
        /// Gets or sets <c>Attributes</c>.
        /// </summary>
        public List<string> Attributes { get; set; }

        /// <summary>
        /// Gets or sets <c>Comment</c>.
        /// </summary>
        public string? Comment { get; set; }

        /// <summary>
        /// Executes public operation <c>Clone</c>.
        /// </summary>
        public CsDelegate Clone()
        {
            return new(CppName, Name, ReturnType.Clone(), Parameters.Select(x => x.Clone()).ToList(), [.. Attributes], Comment);
        }
    }
}
