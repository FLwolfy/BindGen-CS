using System;
using System.Globalization;
using System.Linq;
using System.Text;
using BGCS.Contracts.Interop;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

internal static class StringResultEmitter
{
    internal static string Emit(
        CSharpEmissionPlan plan,
        CSharpStringResultPlan result
    ) {
        CSharpMethodPlan method = plan.methods[result.methodIndex];
        CSharpFacadeMethodPlan signature = result.signature;
        CSharpStringResultLengthPlan? length = result.length;
        int[] inputs = Enumerable.Range(0, method.parameterTypes.Length).Where(index => length is not { output: true } || index != length.parameterIndex).ToArray();
        string[] arguments = method.parameterNames.Select((
            name,
            index
        ) => RecipeConversionEmitter.Input(method, index, name)).ToArray();
        string call = Local("__bgcsCall"), pointer = Local("__bgcsTextResult"), count = Local("__bgcsTextLength"), units = Local("__bgcsTextUnits");
        string runtime = "global::" + CSharpNames.Namespace(plan.runtimeNamespace);
        StringBuilder text = new();
        DocumentationProjector.AppendMethod(text, signature.documentation, signature.documentationContent, signature.parameterNames,
            Enumerable.Range(0, signature.parameterNames.Length), signature.returnType, raw: false, "    ");
        text.Append("    public ").Append(signature.returnType).Append(' ').Append(signature.name).Append('(')
            .Append(string.Join(", ", Enumerable.Range(0, signature.parameterTypes.Length).Select(index => FacadeParameters.Declaration(signature, index)))).AppendLine(")")
            .AppendLine("    {").Append("        using var ").Append(call).AppendLine(" = this.m_session.AcquireCall();");
        TextInputEmitter.Append(text, method, plan.runtimeNamespace, arguments, inputs);
        bool values = ValueInputEmitter.Open(text, method, arguments, inputs);
        bool buffers = BufferInputEmitter.Open(text, method, arguments, inputs);
        if (length is not null)
        {
            text.Append("        ").Append(length.carrierType).Append(' ').Append(count).Append(" = ")
                .Append(length.output ? "default" : arguments[length.parameterIndex]).AppendLine(";");
            arguments[length.parameterIndex] = length.output ? "&" + count : count;
            if (length.output)
                text.Append("        if (((nuint)(&").Append(count).Append(") & ").Append(length.alignment - 1)
                    .AppendLine(") != 0) throw new global::System.ArgumentException(\"The private native string count does not satisfy its established alignment.\");");
        }
        text.Append("        ").Append(method.returnType).Append(' ').Append(pointer).AppendLine(" = null;");
        if (result.releaseIndex is not null) text.AppendLine("        try").AppendLine("        {");
        string indent = result.releaseIndex is not null ? "            " : "        ";
        text.Append(indent).Append(pointer).Append(" = ").Append(DirectOwnedFactoryEmitter.Invoke(plan, result.methodIndex))
            .Append('(').Append(string.Join(", ", arguments)).AppendLine(");");
        BufferInputEmitter.Complete(text, method, inputs, indent);
        text.Append(indent).Append("if (").Append(pointer).Append(" == null) ");
        if (result.allowsNull) text.AppendLine("return null;");
        else text.AppendLine("throw new global::System.InvalidOperationException(\"The native string result contract forbids null.\");");
        string? extent = result.exactCodeUnits?.ToString(CultureInfo.InvariantCulture);
        if (length is not null)
        {
            text.Append(indent).Append("int ").Append(units).Append(" = checked((int)").Append(count).AppendLine(");")
                .Append(indent).Append("if (").Append(units).AppendLine(" < 0) throw new global::System.ArgumentOutOfRangeException(\"nativeStringLength\", \"A native string length cannot be negative.\");");
            if (length.unit == NativeLengthUnit.Bytes && result.codeUnitWidth != 1)
                text.Append(indent).Append("if (").Append(units).Append(" % ").Append(result.codeUnitWidth)
                    .AppendLine(" != 0) throw new global::System.ArgumentException(\"A native string byte length must contain complete code units.\");")
                    .Append(indent).Append(units).Append(" /= ").Append(result.codeUnitWidth).AppendLine(";");
            text.Append(indent).Append("if (").Append(units).Append(" > ").Append(result.maximumCodeUnits)
                .AppendLine(") throw new global::System.ArgumentOutOfRangeException(\"nativeStringLength\", \"The native string exceeds its configured copy cap.\");");
            extent = units;
        }
        string codec = result.encoding switch
        { NativeTextEncoding.Utf8 => "Utf8Codec", NativeTextEncoding.Utf16 => "Utf16Codec", NativeTextEncoding.Utf32 => "Utf32Codec", _ => throw new InvalidOperationException("An unknown string-result codec reached emission.") };
        text.Append(indent).Append("return ").Append(runtime).Append('.').Append(codec).Append(extent is null ? ".Decode(" : ".DecodeLength(")
            .Append("(nint)").Append(pointer).Append(", ").Append(extent ?? result.maximumCodeUnits.ToString(CultureInfo.InvariantCulture));
        if (result.codeUnitWidth != 1)
            text.Append(", ").Append(result.byteOrder switch
            { NativeTextByteOrder.Native => "null", NativeTextByteOrder.LittleEndian => "false", NativeTextByteOrder.BigEndian => "true", _ => throw new InvalidOperationException("An unknown string byte order reached emission.") });
        text.AppendLine(")!;");
        if (result.releaseIndex is int release)
            text.AppendLine("        }").AppendLine("        finally").AppendLine("        {")
                .Append("            if (").Append(pointer).Append(" != null) ").Append(DirectOwnedFactoryEmitter.Invoke(plan, release)).Append("((")
                .Append(plan.methods[release].parameterTypes[0]).Append(')').Append(pointer).AppendLine(");")
                .AppendLine("        }");
        BufferInputEmitter.Close(text, buffers);
        ValueInputEmitter.Close(text, values);
        return text.AppendLine("    }").ToString();

        string Local(string name)
        {
            while (method.parameterNames.Any(parameter => parameter.TrimStart('@') == name)) name += "_";
            return name;
        }
    }
}
