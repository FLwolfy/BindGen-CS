using System.Linq;
using System.Text;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

internal static class DefaultArgumentEmitter
{
    internal static string Emit(CSharpEmissionPlan plan)
    {
        StringBuilder text = new();
        text.Append("public sealed unsafe partial class ").AppendLine(plan.contextName).AppendLine("{");
        if (!plan.defaultConstants.IsEmpty)
        {
            text.AppendLine("    internal static class __BgcsDefaultArguments").AppendLine("    {");
            foreach (CSharpDefaultConstantPlan constant in plan.defaultConstants)
                text.Append("        internal const ").Append(constant.type).Append(' ').Append(constant.name).Append(" = ").Append(constant.expression).AppendLine(";");
            text.AppendLine("    }");
        }
        foreach (CSharpDefaultOverloadPlan overload in plan.defaultOverloads)
        {
            CSharpFacadeMethodPlan signature = overload.signature;
            DocumentationProjector.AppendMethod(text, signature.documentation, signature.documentationContent, signature.parameterNames,
                Enumerable.Range(0, signature.parameterNames.Length), signature.returnType, raw: false, "    ");
            text.Append("    public ").Append(signature.returnType).Append(' ').Append(signature.name).Append('(')
                .Append(string.Join(", ", Enumerable.Range(0, signature.parameterNames.Length).Select(index => FacadeParameters.Declaration(signature, index))))
                .Append(") => this.").Append(overload.targetName).Append('(').Append(string.Join(", ", overload.arguments)).AppendLine(");");
        }
        return text.AppendLine("}").ToString();
    }
}
