using System;
using System.Linq;
using System.Text;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

internal static class SpanMethodEmitter
{
    internal static string Emit(
        CSharpMethodPlan method,
        string runtimeNamespace
    ) {
        int[] inputs = Enumerable.Range(0, method.parameterTypes.Length).ToArray();
        int[] visible = OverloadPlanner.PublicInputs(method, inputs).ToArray();
        string[] arguments = method.parameterNames.Select((
            name,
            index
        ) => RecipeConversionEmitter.Input(method, index, name)).ToArray();
        string result = "__bgcsDirectResult";
        while (method.parameterNames.Any(name => name.TrimStart('@') == result)) result += "_";
        StringBuilder text = new();
        DocumentationProjector.AppendMethod(text, method.documentation, method.documentationContent, method.parameterNames,
            visible, RecipeConversionEmitter.DirectResultType(method), raw: false, "    ");
        text.Append("    public ").Append(RecipeConversionEmitter.DirectResultType(method)).Append(' ').Append(method.name).Append('(')
            .Append(string.Join(", ", visible.Select(index => OverloadPlanner.InputDeclaration(method, index)))).AppendLine(")")
            .AppendLine("    {");
        TextInputEmitter.Append(text, method, runtimeNamespace, arguments, inputs);
        bool values = ValueInputEmitter.Open(text, method, arguments, inputs);
        bool buffers = BufferInputEmitter.Open(text, method, arguments, inputs);
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
