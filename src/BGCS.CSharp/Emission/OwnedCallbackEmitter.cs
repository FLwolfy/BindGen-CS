using System;
using System.Linq;
using System.Text;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

// The context owns the single rooted callback state and thunk; the object forwards
// through that checked path instead of materializing another reverse-call address.
internal static class OwnedCallbackEmitter
{
    internal static string Emit(
        CSharpEmissionPlan plan,
        CSharpCallbackMethodPlan operation
    ) {
        CSharpCallbackReceiverPlan receiver = operation.receiver ?? throw new ArgumentException("An owned callback receiver is required.");
        CSharpMethodPlan method = plan.methods[operation.methodIndex];
        CSharpFacadeMethodPlan signature = receiver.objectSignature;
        StringBuilder text = new();
        DocumentationProjector.AppendMethod(text, method.documentation, method.documentationContent, method.parameterNames,
            signature.inputIndices, signature.returnType, raw: false, "        ");
        text.Append("        public ").Append(signature.returnType).Append(' ').Append(signature.name).Append('(')
            .Append(string.Join(", ", Enumerable.Range(0, signature.parameterNames.Length).Select(index => FacadeParameters.Declaration(signature, index))))
            .AppendLine(")").AppendLine("        {").Append("            ");
        if (signature.returnType != "void") text.Append("return ");
        text.Append("this.m_context.").Append(operation.signature.name).Append('(')
            .Append(string.Join(", ", Enumerable.Range(0, operation.signature.parameterNames.Length).Select(index =>
                operation.signature.inputIndices[index] == receiver.instanceIndex ? "this" : FacadeParameters.Argument(operation.signature, index))))
            .AppendLine(");").AppendLine("        }");
        return text.ToString();
    }
}
