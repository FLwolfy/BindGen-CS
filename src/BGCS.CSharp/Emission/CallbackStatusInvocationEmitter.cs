using System;
using System.Linq;
using System.Text;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

// Reuse the independently checked friendly status operation so callback lifetime does not duplicate native error/ownership logic.
internal static class CallbackStatusInvocationEmitter
{
    internal static void Append(
        StringBuilder text,
        CSharpEmissionPlan plan,
        CSharpCallbackMethodPlan operation,
        CSharpStatusMethodPlan status,
        string[] arguments,
        Func<string, string> local,
        string? nativeInvocation = null,
        Action<StringBuilder>? completeNativeInputs = null
    ) {
        CSharpMethodPlan method = plan.methods[operation.methodIndex];
        string result = local("StatusResult"), nativeFailure = local("NativeFailure"), callbackFailure = local("CapturedFailure"), cleanupFailure = local("CleanupFailure");
        bool owned = status.ownedResult is not null;
        string resultType = operation.signature.returnType;
        foreach (int index in Enumerable.Range(0, operation.signature.parameterNames.Length).Where(index => operation.receiver is null && FacadeParameters.Modifier(operation.signature, index) == "out"))
            text.Append("        global::System.Runtime.CompilerServices.Unsafe.SkipInit(out ").Append(operation.signature.parameterNames[index]).AppendLine(");");
        text.Append("        global::System.Exception? ").Append(nativeFailure).AppendLine(" = null;");
        if (resultType != "void") text.Append("        ").Append(resultType).Append(owned ? "? " : " ").Append(result).AppendLine(" = default;");
        text.AppendLine("        try").AppendLine("        {").Append("            ");
        if (resultType != "void") text.Append(result).Append(" = ");
        text.Append(nativeInvocation ?? "this." + method.name + "(" + string.Join(", ", OverloadPlanner.PublicInputs(method, status.inputIndices)
            .Select(index => (OverloadPlanner.InputModifier(method, index) is { Length: > 0 } modifier ? modifier + " " : "") + arguments[index])) + ")").AppendLine(";");
        completeNativeInputs?.Invoke(text);
        text.AppendLine("        }").Append("        catch (global::System.Exception ").Append(local("StatusFailure")).Append(") { ").Append(nativeFailure)
            .Append(" = ").Append(local("StatusFailure")).AppendLine("; }").AppendLine("        finally").AppendLine("        {");
        foreach (CSharpCallbackInputPlan callback in operation.callbacks.Where(callback => !callback.staticThunk))
            text.Append("            global::System.GC.KeepAlive(").Append(local("State" + callback.parameterIndex)).AppendLine(");");
        text.AppendLine("        }").Append("        global::System.Exception? ").Append(callbackFailure).AppendLine(" = null;");
        foreach (CSharpCallbackInputPlan callback in operation.callbacks)
            text.Append("        ").Append(callbackFailure).Append(" ??= ")
                .Append(callback.staticThunk ? local("State" + callback.parameterIndex) + ".failure" : local("Failure" + callback.parameterIndex)).AppendLine(";");
        if (owned)
        {
            string primaryFailure = local("PrimaryFailure");
            text.Append("        if ((").Append(callbackFailure).Append(" is not null || ").Append(nativeFailure).Append(" is not null) && ").Append(result).AppendLine(" is not null)")
                .AppendLine("        {").Append("            var ").Append(primaryFailure).Append(" = ").Append(callbackFailure).Append(" ?? ").Append(nativeFailure).AppendLine("!;");
            OwnedResultEmitter.AppendFailureCleanup(text, result, primaryFailure, cleanupFailure, local("DisposeFailure"), "            ");
            text.AppendLine("        }");
        }
        text.Append("        if (").Append(callbackFailure).Append(" is not null) global::System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(").Append(callbackFailure).AppendLine(");")
            .Append("        if (").Append(nativeFailure).Append(" is not null) global::System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(").Append(nativeFailure).AppendLine(");");
        if (resultType != "void") text.Append("        return ").Append(result).Append(owned ? "!" : "").AppendLine(";");
    }
}
