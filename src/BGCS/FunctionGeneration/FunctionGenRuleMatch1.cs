namespace BGCS.FunctionGeneration
{
    using BGCS;
    using BGCS.Core.CSharp;
    using BGCS.Core.Mapping;
    using BGCS.CppAst.Model.Declarations;
    using BGCS.CppAst.Model.Types;
    using System.Collections.Generic;

    /// <summary>
    /// Defines the public class <c>FunctionGenRuleMatch</c>.
    /// </summary>
    public abstract class FunctionGenRuleMatch : FunctionGenRule
    {
        /// <summary>
        /// Executes public operation <c>IsMatch</c>.
        /// </summary>
        public abstract bool IsMatch(CppParameter cppParameter, CppType type);

        /// <summary>
        /// Executes public operation <c>CreateParameter</c>.
        /// </summary>
        public override CsParameterInfo CreateParameter(CppParameter cppParameter, ParameterMapping? mapping, string csParamName, CppPrimitiveKind kind, Direction direction, CsCodeGeneratorConfig settings, IList<CppParameter> cppParameters, CsParameterInfo[] csParameterList, int paramIndex, CsFunctionVariation variation)
        {
            if (IsMatch(cppParameter, cppParameter.Type))
            {
                return CreateParameter(cppParameter, mapping, cppParameter.Type, csParamName, kind, direction, settings);
            }

            return CreateDefaultWrapperParameter(cppParameter, mapping, csParamName, kind, direction, settings);
        }

        /// <summary>
        /// Executes public operation <c>CreateParameter</c>.
        /// </summary>
        public abstract CsParameterInfo CreateParameter(CppParameter cppParameter, ParameterMapping? mapping, CppType type, string csParamName, CppPrimitiveKind kind, Direction direction, CsCodeGeneratorConfig settings);
    }
}
