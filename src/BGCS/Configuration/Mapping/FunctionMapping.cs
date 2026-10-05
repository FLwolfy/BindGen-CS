using System.Collections.Generic;

namespace BGCS.Configuration.Mapping
{
    using BGCS.CppAst.Model.Declarations;

    /// <summary>
    /// Overrides one native function's managed name, documentation, parameter defaults, and invocation variations.
    /// </summary>
    public class FunctionMapping
    {
        /// <summary>
        /// Retains function mapping values and their mutable collection containers without copying.
        /// </summary>
        /// <param name="exportedName">
        /// The exact native function identifier.
        /// </param>
        /// <param name="friendlyName">
        /// The managed method identifier override.
        /// </param>
        /// <param name="comment">
        /// The documentation override, or null to retain source-derived documentation.
        /// </param>
        /// <param name="defaults">
        /// Parameter default-expression mappings retained without copying.
        /// </param>
        /// <param name="customVariations">
        /// Managed invocation type overrides retained without copying.
        /// </param>
        /// <param name="parameters">
        /// Ordered parameter mapping overrides retained without copying, or null when absent.
        /// </param>
        public FunctionMapping(
            string exportedName,
            string friendlyName,
            string? comment,
            Dictionary<string, string> defaults,
            List<Dictionary<string, string>> customVariations,
            List<ParameterMapping>? parameters = null
        ) {
            this.exportedName = exportedName;
            this.friendlyName = friendlyName;
            this.comment = comment;
            this.defaults = defaults;
            this.customVariations = customVariations;
            this.parameters = parameters;
        }

        /// <summary>
        /// Gets or sets the exact native identifier used to select this mapping.
        /// </summary>
        public string exportedName { get; set; }
        /// <summary>
        /// Gets or sets the managed identifier override, or null to use configured naming rules.
        /// </summary>
        public string? friendlyName { get; set; }
        /// <summary>
        /// Gets or sets emitted documentation override text, or null to retain source-derived documentation.
        /// </summary>
        public string? comment { get; set; }
        /// <summary>
        /// Gets or sets the optional managed API class that receives the friendly overloads.
        /// Native entry-point stubs remain on the primary configured API class.
        /// </summary>
        public string? containerName { get; set; }
        /// <summary>
        /// Gets or sets managed default-expression spellings keyed by native parameter identifier.
        /// </summary>
        public Dictionary<string, string> defaults { get; set; }
        /// <summary>
        /// Gets or sets ordered custom invocation projections; each dictionary maps native parameter names to managed carrier spellings.
        /// </summary>
        public List<Dictionary<string, string>> customVariations { get; set; }
        /// <summary>
        /// Gets or sets ordered explicit parameter projection overrides, or null when none are configured.
        /// </summary>
        public List<ParameterMapping>? parameters { get; set; }

        /// <summary>
        /// Replaces the current parameter mappings with source-ordered entries using native names and no explicit out override.
        /// </summary>
        /// <param name="function">
        /// The borrowed parsed function supplying parameter names.
        /// </param>
        public void CreateDefaultMappingParameters(CppFunction function)
        {
            this.parameters ??= new(function.parameters.Count);
            this.parameters.Clear();
            foreach (var param in function.parameters)
            {
                ParameterMapping mapping = new(param.name, null, false);
                this.parameters.Add(mapping);
            }
        }
    }

    /// <summary>
    /// Overrides one native parameter's managed identifier and explicit out projection.
    /// </summary>
    public class ParameterMapping
    {
        /// <summary>
        /// Retains one parameter projection override.
        /// </summary>
        /// <param name="exportedName">
        /// The exact native parameter identifier.
        /// </param>
        /// <param name="friendlyName">
        /// The managed identifier override, or null to use naming rules.
        /// </param>
        /// <param name="useOut">
        /// Whether this parameter is explicitly projected as an out argument.
        /// </param>
        public ParameterMapping(
            string exportedName,
            string? friendlyName,
            bool useOut
        ) {
            this.exportedName = exportedName;
            this.friendlyName = friendlyName;
            this.useOut = useOut;
        }

        /// <summary>
        /// Gets or sets the exact native identifier used to select this mapping.
        /// </summary>
        public string exportedName { get; set; }
        /// <summary>
        /// Gets or sets the managed identifier override, or null to use configured naming rules.
        /// </summary>
        public string? friendlyName { get; set; }
        /// <summary>
        /// Gets or sets whether the parameter uses an explicit managed out projection.
        /// </summary>
        public bool useOut { get; set; }
    }
}
