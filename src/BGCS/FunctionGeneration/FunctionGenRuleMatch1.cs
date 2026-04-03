namespace BGCS.FunctionGeneration
{
    using BGCS;
    using BGCS.Core.CSharp;
    using BGCS.Core.Mapping;
    using BGCS.CppAst.Model.Declarations;
    using BGCS.CppAst.Model.Types;
    using System.Collections.Generic;

    public abstract class FunctionGenRuleMatch : FunctionGenRule
    {
        public abstract bool IsMatch(CppParameter cppParameter, CppType type);

        public override CsParameterInfo CreateParameter(CppParameter cppParameter, ParameterMapping? mapping, string csParamName, CppPrimitiveKind kind, Direction direction, CsCodeGeneratorConfig settings, IList<CppParameter> cppParameters, CsParameterInfo[] csParameterList, int paramIndex, CsFunctionVariation variation)
        {
            if (IsMatch(cppParameter, cppParameter.Type))
            {
                return CreateParameter(cppParameter, mapping, cppParameter.Type, csParamName, kind, direction, settings);
            }

            return CreateDefaultWrapperParameter(cppParameter, mapping, csParamName, kind, direction, settings);
        }

        public abstract CsParameterInfo CreateParameter(CppParameter cppParameter, ParameterMapping? mapping, CppType type, string csParamName, CppPrimitiveKind kind, Direction direction, CsCodeGeneratorConfig settings);
    }
}