using System.Linq;
using BGCS.Core.Collections;

namespace BGCS.CSharp
{
    using System.Collections.Generic;
    using Newtonsoft.Json;

    /// <summary>
    /// Groups mutable analyzed overloads under one managed function name.
    /// </summary>
    public class CsFunction : ICloneable<CsFunction>
    {
        /// <summary>
        /// Retains the managed function identity and initializes its overload group.
        /// </summary>
        /// <param name="name">
        /// The managed function identifier.
        /// </param>
        /// <param name="comment">
        /// The emitted function documentation fragment, or null when absent.
        /// </param>
        /// <param name="overloads">
        /// The ordered overload list retained without copying.
        /// </param>
        [JsonConstructor]
        public CsFunction(
            string name,
            string? comment,
            List<CsFunctionOverload> overloads
        ) {
            this.name = name;
            this.comment = comment;
            this.overloads = overloads;
        }

        /// <summary>
        /// Retains the managed function identity and initializes its overload group.
        /// </summary>
        /// <param name="name">
        /// The managed function identifier.
        /// </param>
        /// <param name="comment">
        /// The emitted function documentation fragment, or null when absent.
        /// </param>
        public CsFunction(
            string name,
            string? comment
        ) {
            this.name = name;
            this.comment = comment;
            this.overloads = new();
        }

        /// <summary>
        /// Gets or sets the managed identifier shared by this overload group.
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// Gets or sets the emitted function documentation fragment, or null when absent.
        /// </summary>
        public string? comment { get; set; }
        /// <summary>
        /// Gets or sets mutable overload descriptors in analysis order.
        /// </summary>
        public List<CsFunctionOverload> overloads { get; set; }

        /// <summary>
        /// Returns the managed function identifier for diagnostic display.
        /// </summary>
        /// <returns>
        /// The current managed function name.
        /// </returns>
        public override string ToString()
        {
            return this.name;
        }

        /// <summary>
        /// Clones each overload and its mutable managed projection while retaining the group's immutable text.
        /// </summary>
        /// <returns>
        /// A separately mutable overload group following the overload cloning policy.
        /// </returns>
        public CsFunction Clone()
        {
            return new(this.name, this.comment, this.overloads.Select(overload => overload.Clone()).ToList());
        }
    }
}
