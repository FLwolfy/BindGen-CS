using BGCS.Core.Collections;

namespace BGCS.CSharp
{
    using Newtonsoft.Json;

    /// <summary>
    /// Retains a mutable managed generic parameter identifier and its constraint expression.
    /// </summary>
    public class CsGenericParameterInfo : ICloneable<CsGenericParameterInfo>
    {
        /// <summary>
        /// Captures a managed generic parameter projection.
        /// </summary>
        /// <param name="name">
        /// The managed parameter identifier.
        /// </param>
        /// <param name="constrain">
        /// The managed constraint expression without normalization.
        /// </param>
        [JsonConstructor]
        public CsGenericParameterInfo(
            string name,
            string constrain
        ) {
            this.name = name;
            this.constrain = constrain;
        }

        /// <summary>
        /// Gets or sets the managed generic parameter identifier.
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// Gets or sets the managed generic constraint expression.
        /// </summary>
        public string constrain { get; set; }

        /// <summary>
        /// Copies the generic parameter identifier and constraint into an independent mutable projection.
        /// </summary>
        /// <returns>
        /// A separately mutable descriptor retaining the same immutable text values.
        /// </returns>
        public CsGenericParameterInfo Clone()
        {
            return new CsGenericParameterInfo(this.name, this.constrain);
        }
    }
}
