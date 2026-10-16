using System;
using System.Linq;
using System.Text;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

internal static class DirectReceiverEmitter
{
    internal static string EmitContext(
        CSharpEmissionPlan plan,
        CSharpReceiverMethodPlan operation
    ) {
        CSharpMethodPlan method = plan.methods[operation.methodIndex];
        CSharpFacadeMethodPlan signature = operation.contextSignature;
        string receiver = method.parameterNames[operation.receiverIndex];
        StringBuilder text = new();
        DocumentationProjector.AppendMethod(text, signature.documentation, signature.documentationContent, signature.parameterNames,
            Enumerable.Range(0, signature.parameterNames.Length), signature.returnType, raw: false, "    ");
        text.Append("    public ").Append(signature.returnType).Append(' ').Append(signature.name).Append('(')
            .Append(string.Join(", ", Enumerable.Range(0, signature.parameterTypes.Length).Select(index => FacadeParameters.Declaration(signature, index)))).AppendLine(")")
            .AppendLine("    {")
            .Append("        global::System.ArgumentNullException.ThrowIfNull(").Append(receiver).AppendLine(");")
            .Append("        if (!").Append(receiver).AppendLine(".__BgcsBelongsTo(this)) throw new global::System.ArgumentException(\"The native object belongs to another binding context.\");")
            .Append("        ");
        if (signature.returnType != "void") text.Append("return ");
        text.Append(receiver).Append('.').Append(operation.objectSignature.name).Append('(')
            .Append(string.Join(", ", Enumerable.Range(0, operation.objectSignature.parameterTypes.Length).Select(index => FacadeParameters.Argument(operation.objectSignature, index))))
            .AppendLine(");").AppendLine("    }");
        return text.ToString();
    }

    internal static string EmitObjectMethod(
        CSharpEmissionPlan plan,
        CSharpReceiverMethodPlan operation
    ) {
        CSharpMethodPlan method = plan.methods[operation.methodIndex];
        CSharpFacadeMethodPlan signature = operation.objectSignature;
        int[] inputs = Enumerable.Range(0, method.parameterTypes.Length).Where(index => index != operation.receiverIndex).ToArray();
        string[] arguments = method.parameterNames.ToArray();
        string address = Local(method, "__bgcsReceiverAddress"), returned = Local(method, "__bgcsReceiverResult");
        StringBuilder text = new();
        DocumentationProjector.AppendMethod(text, signature.documentation, signature.documentationContent, signature.parameterNames,
            Enumerable.Range(0, signature.parameterNames.Length), signature.returnType, raw: false, "        ");
        text.Append("        public ").Append(signature.returnType).Append(' ').Append(signature.name).Append('(')
            .Append(string.Join(", ", Enumerable.Range(0, signature.parameterTypes.Length).Select(index => FacadeParameters.Declaration(signature, index)))).AppendLine(")")
            .AppendLine("        {");
        if (operation.requiredReleaseDomain is string domain)
            text.Append("            if (!global::System.String.Equals(m_handle.releaseDomain, ").Append(ContextEmitter.Literal(domain))
                .AppendLine(", global::System.StringComparison.Ordinal)) throw new global::System.ArgumentException(\"The native receiver allocation/release domain does not match this member.\");");
        foreach (int input in OverloadPlanner.PublicInputs(method, inputs).Where(input => !ValueInputEmitter.IsAddress(method, input)
            && !method.stringInputs.Any(item => item.parameterIndex == input)))
        {
            string local = Local(method, "__bgcsReceiverInput" + input);
            text.Append("            ").Append(method.parameterTypes[input]).Append(' ').Append(local).Append(" = ")
                .Append(RecipeConversionEmitter.Input(method, input, method.parameterNames[input])).AppendLine(";");
            arguments[input] = local;
        }
        bool values = ValueInputEmitter.Open(text, method, arguments, inputs, "            ", captureAddresses: true);
        bool buffers = BufferInputEmitter.Open(text, method, arguments, inputs, "            ", captureAddresses: true);
        string invoke = "m_context." + Helper(operation.methodIndex) + "(" + string.Join(", ", new[] { address }.Concat(TextInputEmitter.ForwardedInputs(method, inputs).Select(input => arguments[input]))) + ")";
        text.Append("            ");
        if (signature.returnType != "void") text.Append(signature.returnType).Append(' ').Append(returned).Append(" = ");
        text.Append("m_handle.Invoke(").Append(address).Append(" => ");
        if (signature.returnType == "void") text.Append("{ ").Append(invoke).Append("; return 0; }");
        else text.Append(invoke);
        text.AppendLine(");");
        BufferInputEmitter.Complete(text, method, inputs, "            ");
        if (signature.returnType != "void") text.Append("            return ").Append(returned).AppendLine(";");
        BufferInputEmitter.Close(text, buffers, "            ");
        ValueInputEmitter.Close(text, values, "            ");
        return text.AppendLine("        }").ToString();
    }

    internal static string EmitHelper(
        CSharpEmissionPlan plan,
        CSharpReceiverMethodPlan operation
    ) {
        CSharpMethodPlan method = plan.methods[operation.methodIndex];
        int[] inputs = Enumerable.Range(0, method.parameterTypes.Length).Where(index => index != operation.receiverIndex).ToArray();
        string[] arguments = method.parameterNames.ToArray();
        string address = Local(method, "__bgcsReceiverAddress");
        string helper = Helper(operation.methodIndex);
        if (plan.methods.Any(method => method.name.TrimStart('@') == helper)) throw new InvalidOperationException("A direct C receiver helper conflicts with a configured public method.");
        arguments[operation.receiverIndex] = "(" + method.parameterTypes[operation.receiverIndex] + ")" + address;
        foreach (int input in inputs)
            if (ValueInputEmitter.IsAddress(method, input)) arguments[input] = "(" + method.parameterTypes[input] + ")" + arguments[input];
        StringBuilder text = new();
        text.Append("    private ").Append(operation.objectSignature.returnType).Append(' ').Append(helper).Append("(nint ").Append(address);
        foreach (int input in TextInputEmitter.ForwardedInputs(method, inputs))
            text.Append(", ").Append(ValueInputEmitter.IsAddress(method, input) ? "nint" : method.stringInputs.Any(item => item.parameterIndex == input)
                ? OverloadPlanner.InputType(method, input) : method.parameterTypes[input]).Append(' ').Append(method.parameterNames[input]);
        text.AppendLine(")").AppendLine("    {");
        TextInputEmitter.Append(text, method, plan.runtimeNamespace, arguments, inputs);
        text.Append("        ");
        if (method.returnType != "void") text.Append("return ");
        text.Append(RecipeConversionEmitter.DirectResult(method, DirectOwnedFactoryEmitter.Invoke(plan, operation.methodIndex) + "(" + string.Join(", ", arguments) + ")"))
            .AppendLine(";").AppendLine("    }");
        return text.ToString();
    }

    internal static string Helper(int methodIndex) => "__BgcsDirectReceiverCall" + methodIndex;
    private static string Local(
        CSharpMethodPlan method,
        string name
    ) {
        while (method.parameterNames.Any(parameter => parameter.TrimStart('@') == name)) name += "_";
        return name;
    }
}
