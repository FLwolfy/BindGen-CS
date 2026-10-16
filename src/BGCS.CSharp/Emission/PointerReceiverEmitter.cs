using System;
using System.Linq;
using System.Text;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

internal static class PointerReceiverEmitter
{
    internal static string Emit(
        CSharpEmissionPlan plan,
        CSharpPointerFunctionPlan operation
    ) {
        CSharpFacadeMethodPlan signature = operation.signature;
        int receiver = signature.inputIndices.IndexOf(operation.receiverIndex
            ?? throw new InvalidOperationException("An address receiver lost its native parameter identity."));
        if (receiver < 0) throw new InvalidOperationException("An address receiver is absent from its checked forwarding surface.");
        string context = "context";
        while (signature.parameterNames.Any(name => name.TrimStart('@') == context)) context += "_";
        int[] inputs = Enumerable.Range(0, signature.parameterTypes.Length).Where(index => index != receiver).ToArray();
        string contextType = "global::" + CSharpNames.Namespace(plan.api.@namespace) + "." + CSharpNames.Identifier(plan.contextName);
        string parameters = string.Join(", ", new[] { contextType + " " + context }
            .Concat(inputs.Select(index => FacadeParameters.Declaration(signature, index))));
        string arguments = string.Join(", ", Enumerable.Range(0, signature.parameterTypes.Length)
            .Select(index => index == receiver ? "this" : FacadeParameters.Argument(signature, index)));
        StringBuilder text = new();
        text.AppendLine("    /// <summary>Invokes the explicitly selected operation through the supplied API context.</summary>")
            .AppendLine("    /// <remarks>This address does not retain native storage. The caller must keep it valid for the complete native operation.</remarks>")
            .Append("    public ").Append(signature.returnType).Append(' ').Append(signature.name).Append('(').Append(parameters).AppendLine(")")
            .AppendLine("    {").Append("        global::System.ArgumentNullException.ThrowIfNull(").Append(context).AppendLine(");")
            .Append("        ").Append(signature.returnType == "void" ? string.Empty : "return ")
            .Append(context).Append('.').Append(signature.name).Append('(').Append(arguments).AppendLine(");")
            .AppendLine("    }");
        return text.ToString();
    }
}
