namespace BGCS.FunctionGeneration
{
    using BGCS;
    using BGCS.Core.CSharp;
    using BGCS.Core.Mapping;
    using BGCS.CppAst.Model.Declarations;
    using BGCS.CppAst.Model.Types;
    using System.Collections.Generic;

    /// <summary>
    /// Defines the public class <c>FunctionGenRule</c>.
    /// </summary>
    public abstract class FunctionGenRule
    {
        /// <summary>
        /// Executes public operation <c>CreateParameter</c>.
        /// </summary>
        public abstract CsParameterInfo CreateParameter(CppParameter cppParameter, ParameterMapping? mapping, string csParamName, CppPrimitiveKind kind, Direction direction, CsCodeGeneratorConfig settings, IList<CppParameter> cppParameters, CsParameterInfo[] csParameters, int paramIndex, CsFunctionVariation variation);

        /// <summary>
        /// Executes public operation <c>CreateDefaultParameter</c>.
        /// </summary>
        public virtual CsParameterInfo CreateDefaultParameter(CppParameter cppParameter, ParameterMapping? mapping, string csParamName, CppPrimitiveKind kind, Direction direction, CsCodeGeneratorConfig settings)
        {
            return new(csParamName, cppParameter.Type, new(settings.GetCsTypeName(cppParameter.Type), kind), direction);
        }

        /// <summary>
        /// Executes public operation <c>CreateDefaultWrapperParameter</c>.
        /// </summary>
        public virtual CsParameterInfo CreateDefaultWrapperParameter(CppParameter cppParameter, ParameterMapping? mapping, string csParamName, CppPrimitiveKind kind, Direction direction, CsCodeGeneratorConfig settings)
        {
            if (mapping?.UseOut ?? false)
            {
                return new(csParamName, cppParameter.Type, new(settings.GetCsWrapperTypeName(cppParameter.Type).Replace("ref", "out"), kind), direction);
            }
            return new(csParamName, cppParameter.Type, new(settings.GetCsWrapperTypeName(cppParameter.Type), kind), direction);
        }
    }
}
