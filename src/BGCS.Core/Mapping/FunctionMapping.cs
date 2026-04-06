namespace BGCS.Core.Mapping
{
    using CppAst;
    using BGCS.CppAst.Model.Declarations;

    /// <summary>
    /// Defines the public class <c>FunctionMapping</c>.
    /// </summary>
    public class FunctionMapping
    {
        /// <summary>
        /// Initializes a new instance of <see cref="FunctionMapping"/>.
        /// </summary>
        public FunctionMapping(string exportedName, string friendlyName, string? comment, Dictionary<string, string> defaults, List<Dictionary<string, string>> customVariations, List<ParameterMapping>? parameters = null)
        {
            ExportedName = exportedName;
            FriendlyName = friendlyName;
            Comment = comment;
            Defaults = defaults;
            CustomVariations = customVariations;
            Parameters = parameters;
        }

        /// <summary>
        /// Gets or sets <c>ExportedName</c>.
        /// </summary>
        public string ExportedName { get; set; }

        /// <summary>
        /// Gets or sets <c>FriendlyName</c>.
        /// </summary>
        public string? FriendlyName { get; set; }

        /// <summary>
        /// Gets or sets <c>Comment</c>.
        /// </summary>
        public string? Comment { get; set; }

        /// <summary>
        /// Gets or sets <c>Defaults</c>.
        /// </summary>
        public Dictionary<string, string> Defaults { get; set; }

        /// <summary>
        /// Gets or sets <c>CustomVariations</c>.
        /// </summary>
        public List<Dictionary<string, string>> CustomVariations { get; set; }

        /// <summary>
        /// Gets or sets <c>Parameters</c>.
        /// </summary>
        public List<ParameterMapping>? Parameters { get; set; }

        /// <summary>
        /// Executes public operation <c>CreateDefaultMappingParameters</c>.
        /// </summary>
        public void CreateDefaultMappingParameters(CppFunction function)
        {
            Parameters ??= new(function.Parameters.Count);
            Parameters.Clear();
            foreach (var param in function.Parameters)
            {
                ParameterMapping mapping = new(param.Name, null, false);
                Parameters.Add(mapping);
            }
        }
    }

    /// <summary>
    /// Defines the public class <c>ParameterMapping</c>.
    /// </summary>
    public class ParameterMapping
    {
        /// <summary>
        /// Initializes a new instance of <see cref="ParameterMapping"/>.
        /// </summary>
        public ParameterMapping(string exportedName, string? friendlyName, bool useOut)
        {
            ExportedName = exportedName;
            FriendlyName = friendlyName;
            UseOut = useOut;
        }

        /// <summary>
        /// Gets or sets <c>ExportedName</c>.
        /// </summary>
        public string ExportedName { get; set; }

        /// <summary>
        /// Gets or sets <c>FriendlyName</c>.
        /// </summary>
        public string? FriendlyName { get; set; }

        /// <summary>
        /// Gets or sets <c>UseOut</c>.
        /// </summary>
        public bool UseOut { get; set; }
    }
}
