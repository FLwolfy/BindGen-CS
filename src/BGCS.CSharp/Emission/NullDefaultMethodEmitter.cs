using System.Linq;
using System.Text;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

internal static class NullDefaultMethodEmitter
{
    internal static string Emit(
        CSharpEmissionPlan plan,
        CSharpNullDefaultMethodPlan operation
    ) {
        CSharpMethodPlan method = plan.methods[operation.methodIndex];
        CSharpFacadeMethodPlan signature = operation.signature;
        int[] inputs = Enumerable.Range(0, method.parameterTypes.Length).Except(operation.nullInputs.Select(input => input.parameterIndex)).ToArray();
        string[] arguments = method.parameterNames.Select((
            name,
            index
        ) => RecipeConversionEmitter.Input(method, index, name)).ToArray();
        foreach (CSharpNullDefaultInputPlan binding in operation.nullInputs) arguments[binding.parameterIndex] = "(" + binding.rawPointerType + ")0";
        StringBuilder text = new();
        DocumentationProjector.AppendMethod(text, signature.documentation, signature.documentationContent, signature.parameterNames,
            Enumerable.Range(0, signature.parameterNames.Length), signature.returnType, raw: false, "    ");
        text.Append("    public ").Append(signature.returnType).Append(' ').Append(signature.name).Append('(')
            .Append(string.Join(", ", Enumerable.Range(0, signature.parameterTypes.Length).Select(index => FacadeParameters.Declaration(signature, index))))
            .AppendLine(")").AppendLine("    {");
        string call = "__bgcsNullDefaultCall";
        while (method.parameterNames.Any(name => name.TrimStart('@') == call)) call += "_";
        text.Append("        using var ").Append(call).AppendLine(" = this.m_session.AcquireCall();");
        TextInputEmitter.Append(text, method, plan.runtimeNamespace, arguments, inputs);
        bool values = ValueInputEmitter.Open(text, method, arguments, inputs);
        bool buffers = BufferInputEmitter.Open(text, method, arguments, inputs);
        string result = "__bgcsNullDefaultResult";
        while (method.parameterNames.Any(name => name.TrimStart('@') == result)) result += "_";
        text.Append("        ");
        if (method.returnType != "void") text.Append(method.returnType).Append(' ').Append(result).Append(" = ");
        text.Append("this.Raw.").Append(method.name).Append('(').Append(string.Join(", ", arguments)).AppendLine(");");
        BufferInputEmitter.Complete(text, method, inputs);
        if (method.copiedResultIndex is int copiedResult)
            text.Append("        return ").Append(RecipeConversionEmitter.Result(method, copiedResult, ValueInputEmitter.CopiedLocal(method, copiedResult))).AppendLine(";");
        else if (method.returnType != "void") text.Append("        return ").Append(RecipeConversionEmitter.DirectResult(method, result)).AppendLine(";");
        BufferInputEmitter.Close(text, buffers);
        ValueInputEmitter.Close(text, values);
        return text.AppendLine("    }").ToString();
    }
}
