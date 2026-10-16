using System.Linq;
using System.Text;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

internal static class FunctionContainerEmitter
{
    internal static string Emit(CSharpEmissionPlan plan)
    {
        StringBuilder text = new();
        text.Append("public sealed unsafe partial class ").AppendLine(plan.contextName).AppendLine("{");
        foreach (CSharpFunctionContainerPlan container in plan.functionContainers)
            text.Append("    public ").Append(container.viewType).Append(' ').Append(container.name).AppendLine(" => new(this);");
        text.AppendLine("}");
        foreach (CSharpFunctionContainerPlan container in plan.functionContainers)
        {
            text.Append("public readonly unsafe struct ").AppendLine(container.viewType).AppendLine("{")
                .Append("    private readonly ").Append(plan.contextName).AppendLine("? m_context;")
                .Append("    private ").Append(plan.contextName).AppendLine(" Current => this.m_context ?? throw new global::System.InvalidOperationException(\"A function view must be obtained from a binding context.\");")
                .Append("    internal ").Append(container.viewType).Append('(').Append(plan.contextName).AppendLine(" context) => this.m_context = context;");
            foreach (CSharpContainerMethodPlan method in container.methods)
            {
                CSharpFacadeMethodPlan signature = method.signature;
                DocumentationProjector.AppendMethod(text, signature.documentation, signature.documentationContent, signature.parameterNames,
                    Enumerable.Range(0, signature.parameterNames.Length), signature.returnType, raw: false, "    ");
                text.Append("    public ").Append(signature.returnType).Append(' ').Append(signature.name).Append('(')
                    .Append(string.Join(", ", Enumerable.Range(0, signature.parameterTypes.Length).Select(index => FacadeParameters.Declaration(signature, index))))
                    .Append(") => this.Current.").Append(method.targetName).Append('(')
                    .Append(string.Join(", ", method.arguments.IsEmpty
                        ? Enumerable.Range(0, signature.parameterNames.Length).Select(index => FacadeParameters.Argument(signature, index)) : method.arguments)).AppendLine(");");
            }
            text.AppendLine("}");
        }
        return text.ToString();
    }
}
