using System;
using System.Linq;
using System.Text;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

internal static class ValueReceiverEmitter
{
    internal static string Emit(CSharpValueReceiverPlan operation)
    {
        CSharpFacadeMethodPlan signature = operation.signature;
        string context = signature.parameterNames[0];
        StringBuilder text = new();
        DocumentationProjector.AppendMethod(text, signature.documentation, signature.documentationContent,
            signature.parameterNames, Enumerable.Range(0, signature.parameterNames.Length), signature.returnType, raw: false, "    ");
        text.Append("    public ").Append(operation.isReadOnly ? "readonly " : string.Empty).Append(signature.returnType).Append(' ')
            .Append(signature.name).Append('(').Append(string.Join(", ", Enumerable.Range(0, signature.parameterTypes.Length)
                .Select(index => FacadeParameters.Declaration(signature, index)))).AppendLine(")").AppendLine("    {")
            .Append("        global::System.ArgumentNullException.ThrowIfNull(").Append(context).AppendLine(");")
            .Append("        ");
        if (signature.returnType != "void") text.Append("return ");
        text.Append(context).Append('.').Append(operation.target.name).Append('(')
            .Append(string.Join(", ", Enumerable.Range(0, operation.target.parameterNames.Length).Select(index =>
                operation.target.inputIndices[index] == operation.receiverIndex ? (operation.isReadOnly ? "in this" : "ref this")
                    : FacadeParameters.Argument(operation.target, index))))
            .AppendLine(");").AppendLine("    }");
        return text.ToString();
    }
}
