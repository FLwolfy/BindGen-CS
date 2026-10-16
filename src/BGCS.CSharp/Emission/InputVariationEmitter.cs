using System.Linq;
using System.Text;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

internal static class InputVariationEmitter
{
    internal static string Emit(CSharpEmissionPlan plan)
    {
        StringBuilder text = new();
        text.Append("public sealed unsafe partial class ").AppendLine(plan.contextName).AppendLine("{");
        foreach (CSharpInputVariationPlan variation in plan.inputVariations)
        {
            CSharpFacadeMethodPlan signature = variation.signature;
            DocumentationProjector.AppendMethod(text, signature.documentation, signature.documentationContent, signature.parameterNames,
                Enumerable.Range(0, signature.parameterNames.Length), signature.returnType, raw: false, "    ");
            text.Append("    public ").Append(signature.returnType).Append(' ').Append(signature.name).Append('(')
                .Append(string.Join(", ", Enumerable.Range(0, signature.parameterTypes.Length).Select(index => FacadeParameters.Declaration(signature, index))))
                .Append(") => this.").Append(variation.targetName).Append('(').Append(string.Join(", ", variation.arguments)).AppendLine(");");
        }
        return text.AppendLine("}").ToString();
    }
}
