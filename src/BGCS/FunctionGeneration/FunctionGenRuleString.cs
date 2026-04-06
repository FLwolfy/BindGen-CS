namespace BGCS.FunctionGeneration
{
    using BGCS;
    using BGCS.Core.CSharp;
    using BGCS.Core.Mapping;
    using BGCS.CppAst.Model.Declarations;
    using BGCS.CppAst.Model.Types;
    using System.Collections.Generic;

    /// <summary>
    /// Defines the public class <c>FunctionGenRuleString</c>.
    /// </summary>
    public class FunctionGenRuleString : FunctionGenRule
    {
        /// <summary>
        /// Executes public operation <c>CreateParameter</c>.
        /// </summary>
        public override CsParameterInfo CreateParameter(CppParameter cppParameter, ParameterMapping? mapping, string csParamName, CppPrimitiveKind kind, Direction direction, CsCodeGeneratorConfig settings, IList<CppParameter> cppParameters, CsParameterInfo[] csParameterList, int paramIndex, CsFunctionVariation variation)
        {
            if (cppParameter.Type is CppArrayType arrayType && arrayType.ElementType.IsString(settings, out var stringKind0))
            {
                return new(csParamName, cppParameter.Type, new("string[]", stringKind0), direction);
            }

            if (cppParameter.Type.IsString(settings, out var stringKind1))
            {
                return new(csParamName, cppParameter.Type, new(direction == Direction.InOut ? "ref string" : "string", stringKind1), direction);
            }

            return CreateDefaultWrapperParameter(cppParameter, mapping, csParamName, kind, direction, settings);
        }
    }
}
