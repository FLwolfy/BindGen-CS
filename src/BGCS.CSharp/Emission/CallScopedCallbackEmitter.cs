using System;
using System.Linq;
using System.Text;
using BGCS.Contracts;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

internal static class CallScopedCallbackEmitter
{
    internal static string Emit(
        CSharpEmissionPlan plan,
        CSharpCallbackMethodPlan operation
    ) {
        CSharpMethodPlan method = plan.methods[operation.methodIndex];
        string runtime = "global::" + CSharpNames.Namespace(plan.runtimeNamespace);
        StringBuilder text = new();
        text.Append("public sealed unsafe partial class ").Append(plan.contextName).AppendLine().AppendLine("{");
        foreach (CSharpCallbackInputPlan callback in operation.callbacks)
        {
            if (callback.abiDelegateType is not null) continue;
            text.AppendLine("    /// <summary>Executes within the explicitly declared native call lifetime; pointer arguments are borrowed for that callback only.</summary>");
            if (!callback.staticThunk)
                text.Append("    [global::System.Runtime.InteropServices.UnmanagedFunctionPointer(global::System.Runtime.InteropServices.CallingConvention.")
                    .Append(callback.callingConvention == "Stdcall" ? "StdCall" : "Cdecl").AppendLine(")]");
            text.Append("    public delegate ").Append(callback.resultType).Append(' ').Append(callback.delegateName).Append('(')
                .Append(string.Join(", ", callback.publicIndices.Select(index => callback.parameterTypes[index] + " argument" + index))).AppendLine(");");
            if (!callback.staticThunk) continue;
            text.Append("    private sealed class ").Append(callback.stateName).AppendLine().AppendLine("    {")
                .Append("        internal readonly ").Append(callback.delegateName).AppendLine(" target;")
                .AppendLine("        internal global::System.Exception? failure;")
                .Append("        internal ").Append(callback.stateName).Append('(').Append(callback.delegateName).AppendLine(" target) => this.target = target;")
                .AppendLine("    }")
                .Append("    [global::System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new global::System.Type[] { typeof(global::System.Runtime.CompilerServices.CallConv")
                .Append(callback.callingConvention).AppendLine(") })]")
                .Append("    private static ").Append(callback.resultType).Append(' ').Append(callback.thunkName).Append('(')
                .Append(string.Join(", ", callback.parameterTypes.Select((
                    type,
                    index
                ) => type + " argument" + index))).AppendLine(")")
                .AppendLine("    {").Append("        ").Append(callback.stateName).AppendLine("? state = null;")
                .AppendLine("        try").AppendLine("        {")
                .Append("            if (!").Append(runtime).Append(".CallbackRegistry<").Append(callback.stateName)
                .Append(">.TryGet((nint)argument").Append(callback.callableUserDataIndex).AppendLine(", out state))")
                .Append("                ").AppendLine(Fallback(callback));
            text.Append("            ");
            if (callback.resultType != "void") text.Append("return ");
            text.Append("state.target(").Append(string.Join(", ", callback.publicIndices.Select(index => "argument" + index))).AppendLine(");")
                .AppendLine("        }").AppendLine("        catch (global::System.Exception exception)").AppendLine("        {")
                .AppendLine("            if (state is not null) global::System.Threading.Interlocked.CompareExchange(ref state.failure, exception, null);")
                .Append("            ").AppendLine(Fallback(callback)).AppendLine("        }").AppendLine("    }");
        }
        DocumentationProjector.AppendMethod(text, method.documentation, method.documentationContent, method.parameterNames,
            operation.publicIndices, operation.signature.returnType, raw: false, "    ");
        if (operation.signature.parameterTypes.Any(type => type.EndsWith('*')))
            text.AppendLine("    /// <remarks>Pointer inputs retain the original unsafe native API. The caller supplies their valid storage and native ownership.</remarks>");
        text.Append("    public ").Append(operation.signature.returnType).Append(' ').Append(method.name).Append('(')
            .Append(string.Join(", ", Enumerable.Range(0, operation.signature.parameterNames.Length).Select(index => FacadeParameters.Declaration(operation.signature, index)))).AppendLine(")")
            .AppendLine("    {");
        string Local(string suffix)
        {
            string name = "__bgcsCallback" + suffix;
            while (method.parameterNames.Any(parameter => parameter.TrimStart('@') == name)) name += "_";
            return name;
        }
        string call = Local("Call"), result = Local("Result");
        if (operation.receiver is { } receiver)
        {
            string owner = method.parameterNames[receiver.instanceIndex];
            text.Append("        global::System.ArgumentNullException.ThrowIfNull(").Append(owner).AppendLine(");")
                .Append("        if (!").Append(owner).AppendLine(".__BgcsBelongsTo(this)) throw new global::System.ArgumentException(\"The native callback receiver belongs to another binding context.\");");
        }
        text.Append("        using var ").Append(call).AppendLine(" = this.m_session.AcquireCall();");
        string[] arguments = operation.statusOperation is null
            ? method.parameterNames.Select((
                name,
                index
            ) => RecipeConversionEmitter.Input(method, index, name)).ToArray() : method.parameterNames.ToArray();
        foreach (CSharpCallbackInputPlan callback in operation.callbacks)
        {
            string parameter = method.parameterNames[callback.parameterIndex], state = Local("State" + callback.parameterIndex), root = Local("Root" + callback.parameterIndex);
            text.Append("        global::System.ArgumentNullException.ThrowIfNull(").Append(parameter).AppendLine(");");
            if (callback.staticThunk)
            {
                text.Append("        var ").Append(state).Append(" = new ").Append(callback.stateName).Append('(').Append(parameter).AppendLine(");")
                    .Append("        using var ").Append(root).Append(" = ").Append(runtime).Append(".CallbackRegistry<").Append(callback.stateName).Append(">.Register(").Append(state).AppendLine(");");
                string pointer = Local("Pointer" + callback.parameterIndex), token = Local("Token" + callback.parameterIndex);
                text.Append("        nint ").Append(pointer).Append(" = (nint)((").Append(method.parameterTypes[callback.parameterIndex]).Append(")&").Append(callback.thunkName).AppendLine(");")
                    .Append("        nint ").Append(token).Append(" = ").Append(root).AppendLine(".token;");
                arguments[callback.parameterIndex] = "(" + method.parameterTypes[callback.parameterIndex] + ")" + pointer;
                arguments[callback.userDataIndex] = "(" + method.parameterTypes[callback.userDataIndex] + ")" + token;
            }
            else
            {
                string failure = Local("Failure" + callback.parameterIndex);
                string exception = Local("Exception" + callback.parameterIndex);
                string Argument(int index) => Local("Argument" + callback.parameterIndex + "_" + index);
                text.Append("        global::System.Exception? ").Append(failure).AppendLine(" = null;")
                    .Append("        ").Append(callback.abiDelegateType ?? callback.delegateName).Append(' ').Append(state).Append(" = (")
                    .Append(string.Join(", ", callback.parameterTypes.Select((
                        _,
                        index
                    ) => Argument(index)))).AppendLine(") =>")
                    .AppendLine("        {").AppendLine("            try").AppendLine("            {").Append("                ");
                if (callback.resultType != "void") text.Append("return ");
                text.Append(parameter).Append('(').Append(string.Join(", ", callback.publicIndices.Select(Argument))).AppendLine(");")
                    .AppendLine("            }").Append("            catch (global::System.Exception ").Append(exception).AppendLine(")").AppendLine("            {")
                    .Append("                global::System.Threading.Interlocked.CompareExchange(ref ").Append(failure).Append(", ").Append(exception).AppendLine(", null);")
                    .Append("                ").AppendLine(Fallback(callback)).AppendLine("            }").AppendLine("        };");
                string pointer = Local("Pointer" + callback.parameterIndex);
                text.Append("        nint ").Append(pointer).Append(" = global::System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(").Append(state).AppendLine(");");
                arguments[callback.parameterIndex] = "(" + method.parameterTypes[callback.parameterIndex] + ")" + pointer;
            }
        }
        if (operation.statusOperation is { } statusOperation)
        {
            if (operation.receiver is { } ownedReceiver)
            {
                int[] inputs = statusOperation.inputIndices.Where(index => index != ownedReceiver.instanceIndex).ToArray();
                bool receiverValues = ValueInputEmitter.Open(text, method, arguments, inputs, captureAddresses: true);
                bool receiverBuffers = BufferInputEmitter.Open(text, method, arguments, inputs, captureAddresses: true);
                string address = Local("ReceiverAddress");
                string helper = "this." + OwnedObjectEmitter.Helper(operation.methodIndex) + "(" + string.Join(", ", new[] { address }.Concat(TextInputEmitter.ForwardedInputs(method, inputs).Select(index => arguments[index]))) + ")";
                string invocation = method.parameterNames[ownedReceiver.instanceIndex] + ".Invoke(" + address + " => "
                    + (operation.signature.returnType == "void" ? "{ " + helper + "; return 0; }" : helper) + ")";
                CallbackStatusInvocationEmitter.Append(text, plan, operation, statusOperation, arguments, Local, invocation,
                    target => BufferInputEmitter.Complete(target, method, inputs, "            "));
                BufferInputEmitter.Close(text, receiverBuffers);
                ValueInputEmitter.Close(text, receiverValues);
            }
            else CallbackStatusInvocationEmitter.Append(text, plan, operation, statusOperation, arguments, Local);
            return text.AppendLine("    }").AppendLine("}").ToString();
        }
        TextInputEmitter.Append(text, method, plan.runtimeNamespace, arguments);
        int[] nativeInputs = Enumerable.Range(0, method.parameterTypes.Length).ToArray();
        bool values = ValueInputEmitter.Open(text, method, arguments, nativeInputs);
        bool buffers = BufferInputEmitter.Open(text, method, arguments, nativeInputs);
        if (method.returnType != "void") text.Append("        ").Append(method.returnType).Append(' ').Append(result).AppendLine(";");
        text.AppendLine("        try").AppendLine("        {").Append("            ");
        if (method.returnType != "void") text.Append(result).Append(" = ");
        text.Append(plan.importMode == BindingImportMode.FunctionTable ? "this.m_function" + operation.methodIndex
            : "global::" + CSharpNames.Namespace(plan.api.@namespace) + "." + plan.contextName + ".NativeImports.Function" + operation.methodIndex)
            .Append('(').Append(string.Join(", ", arguments)).AppendLine(");");
        BufferInputEmitter.Complete(text, method, nativeInputs, "            ");
        text.AppendLine("        }").AppendLine("        finally").AppendLine("        {");
        foreach (CSharpCallbackInputPlan callback in operation.callbacks.Where(callback => !callback.staticThunk))
            text.Append("            global::System.GC.KeepAlive(").Append(Local("State" + callback.parameterIndex)).AppendLine(");");
        text.AppendLine("        }");
        foreach (CSharpCallbackInputPlan callback in operation.callbacks)
        {
            string failure = callback.staticThunk ? Local("State" + callback.parameterIndex) + ".failure" : Local("Failure" + callback.parameterIndex);
            text.Append("        if (").Append(failure).Append(" is global::System.Exception ").Append(Local("Captured" + callback.parameterIndex))
                .Append(") global::System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(").Append(Local("Captured" + callback.parameterIndex)).AppendLine(");");
        }
        if (method.returnType != "void") text.Append("        return ").Append(RecipeConversionEmitter.DirectResult(method, result)).AppendLine(";");
        BufferInputEmitter.Close(text, buffers);
        ValueInputEmitter.Close(text, values);
        return text.AppendLine("    }").AppendLine("}").ToString();
    }

    private static string Fallback(CSharpCallbackInputPlan callback) => callback.resultType == "void" ? "return;" : "return " + callback.fallbackExpression + ";";
}
