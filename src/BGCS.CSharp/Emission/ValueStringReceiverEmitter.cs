using System;
using System.Linq;
using System.Text;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

internal static class ValueStringReceiverEmitter
{
    internal static string EmitContext(
        CSharpEmissionPlan plan,
        CSharpValueStringResultPlan operation
    ) {
        CSharpValueStringReceiverPlan receiver = operation.receiver ?? throw new ArgumentException("An owned text receiver is required.");
        CSharpFacadeMethodPlan signature = operation.signature;
        string owner = plan.methods[operation.factory.methodIndex].parameterNames[receiver.receiverIndex];
        StringBuilder text = new();
        DocumentationProjector.AppendMethod(text, signature.documentation, signature.documentationContent, signature.parameterNames,
            Enumerable.Range(0, signature.parameterNames.Length), "string", raw: false, "    ");
        return text.Append("    public string ").Append(signature.name).Append('(')
            .Append(string.Join(", ", Enumerable.Range(0, signature.parameterNames.Length).Select(index => FacadeParameters.Declaration(signature, index)))).AppendLine(")")
            .AppendLine("    {").Append("        global::System.ArgumentNullException.ThrowIfNull(").Append(owner).AppendLine(");")
            .Append("        if (!").Append(owner).AppendLine(".__BgcsBelongsTo(this)) throw new global::System.ArgumentException(\"The native text receiver belongs to another binding context.\");")
            .Append("        return ").Append(owner).Append('.').Append(receiver.signature.name).Append('(')
            .Append(string.Join(", ", Enumerable.Range(0, receiver.signature.parameterNames.Length).Select(index => FacadeParameters.Argument(receiver.signature, index))))
            .AppendLine(");").AppendLine("    }").ToString();
    }

    internal static string Emit(
        CSharpEmissionPlan plan,
        CSharpValueStringResultPlan operation
    ) {
        CSharpValueStringReceiverPlan receiver = operation.receiver ?? throw new ArgumentException("An owned text receiver is required.");
        CSharpMethodPlan method = plan.methods[operation.factory.methodIndex];
        CSharpFacadeMethodPlan signature = receiver.signature;
        int[] inputs = operation.factory.inputIndices.Where(index => index != receiver.receiverIndex).ToArray();
        string[] arguments = method.parameterNames.ToArray();
        string address = Local("__bgcsTextReceiverAddress"), result = Local("__bgcsTextMemberResult");
        StringBuilder text = new();
        DocumentationProjector.AppendMethod(text, signature.documentation, signature.documentationContent, signature.parameterNames,
            Enumerable.Range(0, signature.parameterNames.Length), "string", raw: false, "        ");
        text.Append("        public string ").Append(signature.name).Append('(')
            .Append(string.Join(", ", Enumerable.Range(0, signature.parameterNames.Length).Select(index => FacadeParameters.Declaration(signature, index)))).AppendLine(")")
            .AppendLine("        {");
        bool values = ValueInputEmitter.Open(text, method, arguments, inputs, "            ", captureAddresses: true);
        bool buffers = BufferInputEmitter.Open(text, method, arguments, inputs, "            ", captureAddresses: true);
        text.Append("            string ").Append(result).Append(" = this.m_handle.Invoke(").Append(address).Append(" => this.m_context.")
            .Append(ValueStringResultEmitter.Helper(operation.factory.methodIndex)).Append('(')
            .Append(string.Join(", ", new[] { address }.Concat(TextInputEmitter.ForwardedInputs(method, inputs).Select(input => arguments[input])))).AppendLine("));");
        BufferInputEmitter.Complete(text, method, inputs, "            ");
        text.Append("            return ").Append(result).AppendLine(";");
        BufferInputEmitter.Close(text, buffers, "            ");
        ValueInputEmitter.Close(text, values, "            ");
        return text.AppendLine("        }").ToString();

        string Local(string name)
        {
            while (method.parameterNames.Any(parameter => parameter.TrimStart('@') == name)) name += "_";
            return name;
        }
    }
}
