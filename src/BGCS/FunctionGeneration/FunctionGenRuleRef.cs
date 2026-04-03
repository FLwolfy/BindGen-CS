namespace BGCS.FunctionGeneration
{
    using BGCS;
    using BGCS.Core.CSharp;
    using BGCS.Core.Mapping;
    using BGCS.CppAst.Model.Declarations;
    using BGCS.CppAst.Model.Types;

    public class FunctionGenRuleRef : FunctionGenRuleMatch<CppArrayType>
    {
        public override CsParameterInfo CreateParameter(CppParameter cppParameter, ParameterMapping? mapping, CppArrayType type, string csParamName, CppPrimitiveKind kind, Direction direction, CsCodeGeneratorConfig settings)
        {
            if (mapping != null && mapping.UseOut)
            {
                return new(csParamName, cppParameter.Type, new("out " + settings.GetCsTypeName(type.ElementType), kind), direction);
            }
            return new(csParamName, cppParameter.Type, new("ref " + settings.GetCsTypeName(type.ElementType), kind), direction);
        }

        public override bool IsMatch(CppParameter cppParameter, CppArrayType type)
        {
            return type.Size > 0;
        }
    }
}