using System.Linq;
using System.Text;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

internal static class AliasEmitter
{
    internal static string Emit(CSharpEmissionPlan plan)
    {
        StringBuilder text = new();
        text.Append("public sealed unsafe partial class ").AppendLine(plan.contextName).AppendLine("{");
        foreach (CSharpAliasPlan alias in plan.aliases)
        {
            if (alias.friendly is not { } signature) continue;
            DocumentationProjector.AppendMethod(text, signature.documentation, signature.documentationContent, signature.parameterNames,
                Enumerable.Range(0, signature.parameterNames.Length), signature.returnType, raw: false, "    ");
            text.Append("    public ").Append(signature.returnType).Append(' ').Append(signature.name).Append('(')
                .Append(string.Join(", ", Enumerable.Range(0, signature.parameterTypes.Length).Select(index => FacadeParameters.Declaration(signature, index))))
                .Append(") => this.").Append(plan.methods[alias.targetIndex].name).Append('(')
                .Append(string.Join(", ", Enumerable.Range(0, signature.parameterNames.Length).Select(index => FacadeParameters.Argument(signature, index)))).AppendLine(");");
        }
        text.AppendLine("    public sealed partial class RawApi").AppendLine("    {");
        foreach (CSharpAliasPlan alias in plan.aliases)
        {
            CSharpMethodPlan signature = alias.raw;
            DocumentationProjector.AppendMethod(text, signature.documentation, signature.documentationContent, signature.parameterNames,
                Enumerable.Range(0, signature.parameterNames.Length), signature.returnType, raw: true, "        ");
            text.Append("        public ").Append(signature.returnType).Append(' ').Append(signature.name).Append('(')
                .Append(string.Join(", ", signature.parameterTypes.Zip(signature.parameterNames, (
                    type,
                    name
                ) => type + " " + name)))
                .Append(") => this.").Append(plan.methods[alias.targetIndex].name).Append('(')
                .Append(string.Join(", ", signature.parameterNames)).AppendLine(");");
        }
        return text.AppendLine("    }").AppendLine("}").ToString();
    }
}
