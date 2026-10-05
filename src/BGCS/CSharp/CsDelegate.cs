using System.Linq;
using BGCS.Core.Collections;

namespace BGCS.CSharp
{
    using System.Collections.Generic;
    using Newtonsoft.Json;

    /// <summary>
    /// Retains the mutable managed callback projection used during binding analysis.
    /// </summary>
    public class CsDelegate : IHasIdentifier, ICloneable<CsDelegate>
    {
        /// <summary>
        /// Retains a callback signature and its mutable analysis projections.
        /// </summary>
        /// <param name="cppName">
        /// The original native callback type name.
        /// </param>
        /// <param name="name">
        /// The managed delegate identifier.
        /// </param>
        /// <param name="returnType">
        /// The retained mutable return-type projection.
        /// </param>
        /// <param name="parameters">
        /// The ordered mutable parameter descriptors and their list, retained without copying.
        /// </param>
        /// <param name="attributes">
        /// Managed attribute fragments retained without copying; null creates an empty list.
        /// </param>
        /// <param name="comment">
        /// The emitted documentation fragment, or null when absent.
        /// </param>
        [JsonConstructor]
        public CsDelegate(
            string cppName,
            string name,
            CsType returnType,
            List<CsParameterInfo> parameters,
            List<string>? attributes = null,
            string? comment = null
        ) {
            this.cppName = cppName;
            this.name = name;
            this.returnType = returnType;
            this.parameters = parameters;
            this.attributes = attributes ?? [];
            this.comment = comment;
        }

        /// <summary>
        /// Retains a callback signature and its mutable analysis projections.
        /// </summary>
        /// <param name="cppName">
        /// The original native callback type name.
        /// </param>
        /// <param name="name">
        /// The managed delegate identifier.
        /// </param>
        /// <param name="returnType">
        /// The retained mutable return-type projection.
        /// </param>
        /// <param name="parameters">
        /// The ordered mutable parameter descriptors and their list, retained without copying.
        /// </param>
        public CsDelegate(
            string cppName,
            string name,
            CsType returnType,
            List<CsParameterInfo> parameters
        ) {
            this.cppName = cppName;
            this.name = name;
            this.returnType = returnType;
            this.parameters = parameters;
            this.attributes = [];
        }

        /// <summary>
        /// Gets the managed delegate identifier used for metadata matching.
        /// </summary>
        public string identifier => this.name;
        /// <summary>
        /// Gets or sets the original native callback type name.
        /// </summary>
        public string cppName { get; set; }
        /// <summary>
        /// Gets or sets the managed delegate identifier.
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// Gets or sets the mutable managed return-type projection.
        /// </summary>
        public CsType returnType { get; set; }
        /// <summary>
        /// Gets or sets the mutable parameter projections in declaration order.
        /// </summary>
        public List<CsParameterInfo> parameters { get; set; }
        /// <summary>
        /// Gets or sets managed delegate attribute fragments in emission order.
        /// </summary>
        public List<string> attributes { get; set; }
        /// <summary>
        /// Gets or sets the emitted delegate documentation fragment, or null when absent.
        /// </summary>
        public string? comment { get; set; }

        /// <summary>
        /// Clones the return type, parameter descriptors, and attribute container while retaining immutable text and borrowed native analysis references.
        /// </summary>
        /// <returns>
        /// A separately mutable callback projection; referenced parser metadata follows the parameter cloning policy.
        /// </returns>
        public CsDelegate Clone()
        {
            return new(this.cppName, this.name, this.returnType.Clone(), this.parameters.Select(x => x.Clone()).ToList(), [.. this.attributes], this.comment);
        }
    }
}
