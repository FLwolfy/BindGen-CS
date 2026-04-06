namespace BGCS.Core.CSharp
{
    using Newtonsoft.Json;

    /// <summary>
    /// Defines the public class <c>CsGenericParameterInfo</c>.
    /// </summary>
    public class CsGenericParameterInfo : ICloneable<CsGenericParameterInfo>
    {
        [JsonConstructor]
        /// <summary>
        /// Initializes a new instance of <see cref="CsGenericParameterInfo"/>.
        /// </summary>
        public CsGenericParameterInfo(string name, string constrain)
        {
            Name = name;
            Constrain = constrain;
        }

        /// <summary>
        /// Gets or sets <c>Name</c>.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets <c>Constrain</c>.
        /// </summary>
        public string Constrain { get; set; }

        /// <summary>
        /// Executes public operation <c>Clone</c>.
        /// </summary>
        public CsGenericParameterInfo Clone()
        {
            return new CsGenericParameterInfo(Name, Constrain);
        }
    }
}
