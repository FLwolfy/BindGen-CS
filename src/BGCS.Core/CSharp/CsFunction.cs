namespace BGCS.Core.CSharp
{
    using System;
    using System.Collections.Generic;
    using Newtonsoft.Json;

    /// <summary>
    /// Defines the public class <c>CsFunction</c>.
    /// </summary>
    public class CsFunction : ICloneable<CsFunction>
    {
        [JsonConstructor]
        /// <summary>
        /// Initializes a new instance of <see cref="CsFunction"/>.
        /// </summary>
        public CsFunction(string name, string? comment, List<CsFunctionOverload> overloads)
        {
            Name = name;
            Comment = comment;
            Overloads = overloads;
        }

        /// <summary>
        /// Executes public operation <c>CsFunction</c>.
        /// </summary>
        public CsFunction(string name, string? comment)
        {
            Name = name;
            Comment = comment;
            Overloads = new();
        }

        /// <summary>
        /// Gets or sets <c>Name</c>.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets <c>Comment</c>.
        /// </summary>
        public string? Comment { get; set; }

        /// <summary>
        /// Gets or sets <c>Overloads</c>.
        /// </summary>
        public List<CsFunctionOverload> Overloads { get; set; }

        /// <summary>
        /// Executes public operation <c>ToString</c>.
        /// </summary>
        public override string ToString()
        {
            return Name;
        }

        /// <summary>
        /// Executes public operation <c>Clone</c>.
        /// </summary>
        public CsFunction Clone()
        {
            return new(Name, Comment, Overloads.Select(overload => overload.Clone()).ToList());
        }
    }
}
