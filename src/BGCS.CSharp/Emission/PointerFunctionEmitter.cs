using System;
using System.Linq;
using System.Text;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

internal static class PointerFunctionEmitter
{
    internal static string Emit(
        CSharpEmissionPlan plan,
        CSharpPointerFunctionPlan operation
    ) {
        CSharpMethodPlan method = plan.methods[operation.methodIndex];
        CSharpFacadeMethodPlan signature = operation.signature;
        int[] selected = Enumerable.Range(0, method.parameterTypes.Length).ToArray();
        string[] arguments = method.parameterNames.Select((
            name,
            index
        ) => operation.parameters.ContainsKey(index)
            ? "(" + method.parameterTypes[index] + ")" + name + ".Handle" : RecipeConversionEmitter.Input(method, index, name)).ToArray();
        string call = Local("__bgcsAddressCall"), result = Local("__bgcsAddressResult");
        StringBuilder text = new();
        DocumentationProjector.AppendMethod(text, signature.documentation, signature.documentationContent, signature.parameterNames,
            Enumerable.Range(0, signature.parameterNames.Length), signature.returnType, raw: false, "    ");
        text.AppendLine("    /// <remarks>Address carriers do not own or retain native memory. The caller must keep every pointed allocation live throughout native use.</remarks>")
            .Append("    public ").Append(signature.returnType).Append(' ').Append(signature.name).Append('(')
            .Append(string.Join(", ", Enumerable.Range(0, signature.parameterTypes.Length).Select(index => FacadeParameters.Declaration(signature, index))))
            .AppendLine(")").AppendLine("    {").Append("        using var ").Append(call).AppendLine(" = this.m_session.AcquireCall();");
        TextInputEmitter.Append(text, method, plan.runtimeNamespace, arguments, selected);
        bool values = ValueInputEmitter.Open(text, method, arguments, selected);
        bool buffers = BufferInputEmitter.Open(text, method, arguments, selected);
        text.Append("        ");
        if (method.returnType != "void") text.Append(method.returnType).Append(' ').Append(result).Append(" = ");
        text.Append(DirectOwnedFactoryEmitter.Invoke(plan, operation.methodIndex)).Append('(').Append(string.Join(", ", arguments)).AppendLine(");");
        BufferInputEmitter.Complete(text, method, selected);
        if (method.returnType != "void")
        {
            text.Append("        return ");
            if (operation.returnCarrier is null) text.Append(RecipeConversionEmitter.DirectResult(method, result));
            else text.Append("new ").Append(CSharpGeneratedTypeScope.QualifiedReference(plan.api, operation.returnCarrier)).Append("((nint)").Append(result).Append(')');
            text.AppendLine(";");
        }
        BufferInputEmitter.Close(text, buffers);
        ValueInputEmitter.Close(text, values);
        return text.AppendLine("    }").ToString();

        string Local(string candidate)
        {
            while (method.parameterNames.Any(parameter => parameter.TrimStart('@') == candidate)) candidate += "_";
            return candidate;
        }
    }
}
