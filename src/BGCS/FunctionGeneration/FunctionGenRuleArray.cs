namespace BGCS.FunctionGeneration
{
    using BGCS.Core.CSharp;
    using BGCS.Core.Mapping;
    using BGCS.CppAst.Model.Declarations;
    using BGCS.CppAst.Model.Types;

    /// <summary>
    /// Defines the public class <c>FunctionGenRuleArray</c>.
    /// </summary>
    public class FunctionGenRuleArray : FunctionGenRuleMatch<CppArrayType>
    {
        private readonly CsCodeGeneratorConfig config;

        /// <summary>
        /// Initializes a new instance of <see cref="FunctionGenRuleArray"/>.
        /// </summary>
        public FunctionGenRuleArray(CsCodeGeneratorConfig config)
        {
            this.config = config;
        }

        /// <summary>
        /// Executes public operation <c>CreateParameter</c>.
        /// </summary>
        public override CsParameterInfo CreateParameter(CppParameter cppParameter, ParameterMapping? mapping, CppArrayType type, string csParamName, CppPrimitiveKind kind, Direction direction, CsCodeGeneratorConfig settings)
        {
            if (settings.TryGetArrayMapping(type, out var arrayMapping))
            {
                return new(csParamName, cppParameter.Type, new($"ref {arrayMapping}", kind), direction);
            }

            return CreateDefaultWrapperParameter(cppParameter, mapping, csParamName, kind, direction, settings);
        }

        /// <summary>
        /// Executes public operation <c>IsMatch</c>.
        /// </summary>
        public override bool IsMatch(CppParameter cppParameter, CppArrayType type)
        {
            return type.Size > 0 && config.TryGetArrayMapping(type, out _);
        }
    }
}
