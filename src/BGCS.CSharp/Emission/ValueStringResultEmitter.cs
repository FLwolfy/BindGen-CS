using System;
using System.Linq;
using System.Text;
using BGCS.Core.Interop;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

internal static class ValueStringResultEmitter
{
    internal static string Emit(
        CSharpEmissionPlan plan,
        CSharpValueStringResultPlan operation
    ) => operation.receiver is not null ? ValueStringReceiverEmitter.EmitContext(plan, operation) : EmitCore(plan, operation, null);

    internal static string EmitInstanceHelper(
        CSharpEmissionPlan plan,
        CSharpValueStringResultPlan operation
    ) {
        if (operation.receiver is null) throw new ArgumentException("An instance text helper requires an existing owned receiver.");
        return EmitCore(plan, operation, Helper(operation.factory.methodIndex));
    }

    internal static string Helper(int methodIndex) => "__BgcsValueTextReceiver" + methodIndex;

    private static string EmitCore(
        CSharpEmissionPlan plan,
        CSharpValueStringResultPlan operation,
        string? helper
    ) {
        CSharpMethodPlan method = plan.methods[operation.factory.methodIndex];
        CSharpFacadeMethodPlan signature = operation.signature;
        int[] inputs = operation.factory.inputIndices.Where(index => helper is null || index != operation.receiver!.receiverIndex).ToArray();
        string runtime = "global::" + CSharpNames.Namespace(plan.runtimeNamespace);
        string resource = Local("__bgcsValueResource"), call = Local("__bgcsValueCall"), holder = Local("__bgcsTextHolder");
        string acquired = Local("__bgcsValueAcquired"), failure = Local("__bgcsValueFailure"), caught = Local("__bgcsCaughtValueFailure");
        string cleanupFailure = Local("__bgcsValueCleanupFailure"), count = Local("__bgcsNativeTextCount"), units = Local("__bgcsTextUnits");
        string bytes = Local("__bgcsTextBytes"), destination = Local("__bgcsTextDestination"), written = Local("__bgcsTextWritten");
        string[] arguments = method.parameterNames.Select((
            name,
            index
        ) => RecipeConversionEmitter.Input(method, index, name)).ToArray();
        string receiverAddress = Local("__bgcsOwnedTextReceiver");
        if (helper is not null)
        {
            arguments[operation.receiver!.receiverIndex] = "(" + method.parameterTypes[operation.receiver.receiverIndex] + ")" + receiverAddress;
            foreach (int input in inputs.Where(input => ValueInputEmitter.IsAddress(method, input)))
                arguments[input] = "(" + method.parameterTypes[input] + ")" + method.parameterNames[input];
        }
        StringBuilder text = new();
        if (helper is null)
            DocumentationProjector.AppendMethod(text, signature.documentation, signature.documentationContent, signature.parameterNames,
                Enumerable.Range(0, signature.parameterNames.Length), "string", raw: false, "    ");
        text.Append(helper is null ? "    public string " : "    private string ").Append(helper ?? signature.name).Append('(');
        if (helper is null)
            text.Append(string.Join(", ", Enumerable.Range(0, signature.parameterNames.Length).Select(index => FacadeParameters.Declaration(signature, index))));
        else
        {
            text.Append("nint ").Append(receiverAddress);
            foreach (int input in TextInputEmitter.ForwardedInputs(method, inputs))
                text.Append(", ").Append(ValueInputEmitter.IsAddress(method, input) ? "nint" : OverloadPlanner.InputType(method, input)).Append(' ').Append(method.parameterNames[input]);
        }
        text.AppendLine(")").AppendLine("    {").Append("        using var ").Append(resource).AppendLine(" = this.m_session.AcquireResource();")
            .Append("        using var ").Append(call).Append(" = ").Append(resource).AppendLine(".AcquireCall();");
        TextInputEmitter.Append(text, method, plan.runtimeNamespace, arguments, inputs);
        bool values = helper is null && ValueInputEmitter.Open(text, method, arguments, inputs);
        bool buffers = helper is null && BufferInputEmitter.Open(text, method, arguments, inputs);
        text.Append("        ").Append(operation.factory.resultType).Append(' ').Append(holder).AppendLine(" = null;")
            .Append("        bool ").Append(acquired).AppendLine(" = false;")
            .Append("        global::System.Exception? ").Append(failure).AppendLine(" = null;")
            .AppendLine("        try").AppendLine("        {");
        AppendCall(operation.factory, arguments, holder, "            ", acquired + " = true;");
        text.Append("            if (").Append(holder).AppendLine(" == null) throw new global::System.InvalidOperationException(\"A successful native text value factory returned a null owned holder.\");");
        if (helper is null) BufferInputEmitter.Complete(text, method, inputs, "            ");
        text.Append("            ulong ").Append(count).AppendLine(" = 0;");
        AppendCall(operation.count, Inputs(operation.count, holder), count, "            ");
        text.Append("            if (").Append(count).Append(" > ").Append(operation.maximumCodeUnits)
            .AppendLine("UL) throw new global::System.ArgumentOutOfRangeException(\"nativeTextLength\", \"The native text value exceeds the configured copy bound.\");")
            .Append("            int ").Append(units).Append(" = checked((int)").Append(count).AppendLine(");")
            .Append("            byte[] ").Append(bytes).Append(" = new byte[checked(").Append(units).Append(" * ").Append(operation.codeUnitWidth).AppendLine(")];")
            .Append("            fixed (byte* ").Append(destination).Append(" = ").Append(bytes).AppendLine(")")
            .AppendLine("            {")
            .Append("                if (").Append(units).Append(" != 0 && ((nuint)").Append(destination).Append(" & ").Append(operation.copyAlignment - 1)
            .AppendLine(") != 0) throw new global::System.InvalidOperationException(\"The managed native text copy destination does not satisfy its exact native alignment.\");")
            .Append("                ulong ").Append(written).AppendLine(" = 0;");
        AppendCall(operation.copy, Inputs(operation.copy, holder, destination, count), written, "                ");
        text.Append("                if (").Append(written).Append(" != ").Append(count)
            .AppendLine(") throw new global::System.InvalidOperationException(\"The native text bulk copy changed its complete counted extent.\");")
            .Append("                if (").Append(units).AppendLine(" == 0) return string.Empty;");
        string codec = operation.encoding switch
        { NativeTextEncoding.Utf8 => "Utf8Codec", NativeTextEncoding.Utf16 => "Utf16Codec", NativeTextEncoding.Utf32 => "Utf32Codec", _ => throw new InvalidOperationException("Unknown native value string encoding.") };
        text.Append("                return ").Append(runtime).Append('.').Append(codec).Append(".DecodeLength((nint)").Append(destination).Append(", ").Append(units);
        if (operation.codeUnitWidth > 1) text.Append(", ").Append(operation.byteOrder == NativeTextByteOrder.BigEndian ? "true" : "false");
        text.AppendLine(")!;").AppendLine("            }").AppendLine("        }")
            .Append("        catch (global::System.Exception ").Append(caught).AppendLine(")")
            .Append("        { ").Append(failure).Append(" = ").Append(caught).AppendLine("; throw; }")
            .AppendLine("        finally").AppendLine("        {")
            .Append("            if (").Append(acquired).Append(" && ").Append(holder).AppendLine(" != null)")
            .AppendLine("            {").AppendLine("                try").AppendLine("                {");
        AppendCall(operation.release, Inputs(operation.release, holder), null, "                    ");
        text.AppendLine("                }").Append("                catch (global::System.Exception ").Append(cleanupFailure).AppendLine(")")
            .AppendLine("                {").Append("                    ").Append(resource).Append(".RecordUnresolvedCleanup(").Append(call).Append(", ").Append(cleanupFailure).AppendLine(");")
            .Append("                    if (").Append(failure).Append(" is not null) throw new global::System.AggregateException(\"The native text copy and its matching destruction both failed.\", ")
            .Append(failure).Append(", ").Append(cleanupFailure).AppendLine(");")
            .AppendLine("                    throw;").AppendLine("                }").AppendLine("            }").AppendLine("        }");
        BufferInputEmitter.Close(text, buffers);
        ValueInputEmitter.Close(text, values);
        return text.AppendLine("    }").ToString();

        string Local(string name)
        {
            while (method.parameterNames.Any(parameter => parameter.TrimStart('@') == name)) name += "_";
            return name;
        }
        string[] Inputs(
            CSharpScopedStatusCallPlan status,
            params string[] values
        ) {
            string[] inputs = new string[plan.methods[status.methodIndex].parameterTypes.Length];
            for (int index = 0; index < values.Length; index++)
            {
                int input = status.inputIndices[index];
                inputs[input] = "(" + plan.methods[status.methodIndex].parameterTypes[input] + ")" + values[index];
            }
            return inputs;
        }
        void AppendCall(
            CSharpScopedStatusCallPlan status,
            string[] inputs,
            string? output,
            string indent,
            string? afterSuccess = null
        ) {
            string error = Local("__bgcsStepError"), result = Local("__bgcsStepStatus");
            CSharpMethodPlan import = plan.methods[status.methodIndex];
            string[] native = (string[])inputs.Clone();
            native[status.errorIndex] = "&" + error;
            if (status.resultIndex >= 0) native[status.resultIndex] = "&" + output;
            text.Append(indent).AppendLine("{").Append(indent).Append("    ").Append(import.parameterTypes[status.errorIndex][..^1].TrimEnd()).Append(' ').Append(error).AppendLine(" = null;")
                .Append(indent).AppendLine("    try").Append(indent).AppendLine("    {")
                .Append(indent).Append("        int ").Append(result).Append(" = ").Append(DirectOwnedFactoryEmitter.Invoke(plan, status.methodIndex))
                .Append('(').Append(string.Join(", ", native)).AppendLine(");")
                .Append(indent).Append("        if (").Append(result).Append(" != 0) throw new ").Append(runtime).Append(".NativeCallException(")
                .Append(result).Append(", ").Append(ContextEmitter.Literal(import.exportName)).AppendLine(");");
            if (afterSuccess is not null) text.Append(indent).Append("        ").AppendLine(afterSuccess);
            text.Append(indent).AppendLine("    }").Append(indent).AppendLine("    finally").Append(indent).AppendLine("    {")
                .Append(indent).Append("        if (").Append(error).Append(" != null) ").Append(DirectOwnedFactoryEmitter.Invoke(plan, status.errorReleaseIndex))
                .Append('(').Append(error).AppendLine(");")
                .Append(indent).AppendLine("    }").Append(indent).AppendLine("}");
        }
    }
}
