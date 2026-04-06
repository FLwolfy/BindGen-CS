namespace BGCS.FunctionGeneration
{
    using BGCS;
    using BGCS.Core.CSharp;
    using BGCS.Core.Mapping;
    using BGCS.CppAst.Model.Declarations;
    using BGCS.CppAst.Model.Interfaces;
    using BGCS.CppAst.Model.Types;
    using System.Collections.Generic;

    /// <summary>
    /// Defines the public class <c>FunctionGenRuleMatch</c>.
    /// </summary>
    public abstract class FunctionGenRuleMatch<T> : FunctionGenRule where T : ICppElement
    {
        /// <summary>
        /// Executes public operation <c>IsMatch</c>.
        /// </summary>
        public abstract bool IsMatch(CppParameter cppParameter, T type);

        /// <summary>
        /// Executes public operation <c>CreateParameter</c>.
        /// </summary>
        public override CsParameterInfo CreateParameter(CppParameter cppParameter, ParameterMapping? mapping, string csParamName, CppPrimitiveKind kind, Direction direction, CsCodeGeneratorConfig settings, IList<CppParameter> cppParameters, CsParameterInfo[] csParameterList, int paramIndex, CsFunctionVariation variation)
        {
            if (cppParameter.Type is T t && IsMatch(cppParameter, t))
            {
                return CreateParameter(cppParameter, mapping, t, csParamName, kind, direction, settings);
            }

            return CreateDefaultWrapperParameter(cppParameter, mapping, csParamName, kind, direction, settings);
        }

        /// <summary>
        /// Executes public operation <c>CreateParameter</c>.
        /// </summary>
        public abstract CsParameterInfo CreateParameter(CppParameter cppParameter, ParameterMapping? mapping, T type, string csParamName, CppPrimitiveKind kind, Direction direction, CsCodeGeneratorConfig settings);
    }
}
