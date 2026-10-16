using System;
using System.Linq;
using System.Text;
using BGCS.Contracts;
using BGCS.Contracts.Requests;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

internal static class OwnedResultEmitter
{
    internal static string Emit(
        CSharpEmissionPlan plan,
        EmissionRequest request,
        CSharpStatusMethodPlan operation
    )
        => EmitCore(plan, request, operation, null);

    internal static string EmitInstanceHelper(
        CSharpEmissionPlan plan,
        EmissionRequest request,
        CSharpStatusMethodPlan operation,
        string helper
    ) {
        if (operation.instanceIndex < 0) throw new ArgumentException("An owned result instance helper requires an exact receiver.");
        return EmitCore(plan, request, operation, helper);
    }

    internal static string ResultType(
        CSharpEmissionPlan plan,
        CSharpStatusMethodPlan operation
    )
        => operation.ownedResult is { } owned ? owned.objectName ?? "global::" + CSharpNames.Namespace(plan.runtimeNamespace) + ".OwnedNativeHandle"
            : RecipeConversionEmitter.ResultType(plan.methods[operation.methodIndex], operation);

    internal static void AppendFailureCleanup(
        StringBuilder text,
        string result,
        string failure,
        string cleanupFailure,
        string disposeFailure,
        string indent
    ) {
        text.Append(indent).Append("try { ").Append(result).AppendLine(".Dispose(); }")
            .Append(indent).Append("catch (global::System.Exception ").Append(disposeFailure).AppendLine(")")
            .Append(indent).Append("{ throw new global::System.AggregateException(\"The native operation and owned result cleanup both failed.\", ")
            .Append(failure).Append(", ").Append(disposeFailure).AppendLine("); }")
            .Append(indent).Append("if (").Append(result).Append(".releaseFailure is global::System.Exception ").Append(cleanupFailure).AppendLine(")")
            .Append(indent).Append("    throw new global::System.AggregateException(\"The native operation and owned result cleanup both failed.\", ")
            .Append(failure).Append(", ").Append(cleanupFailure).AppendLine(");");
    }

    private static string EmitCore(
        CSharpEmissionPlan plan,
        EmissionRequest request,
        CSharpStatusMethodPlan operation,
        string? instanceHelper
    ) {
        CSharpOwnedResultPlan owned = operation.ownedResult ?? throw new ArgumentException("An owned result plan is required.");
        CSharpMethodPlan method = plan.methods[operation.methodIndex];
        CSharpMethodPlan release = plan.methods[owned.releaseIndex];
        string runtime = "global::" + CSharpNames.Namespace(request.runtimeNamespace);
        bool instance = instanceHelper is not null;
        int[] inputs = operation.inputIndices.Where(index => !instance || index != operation.instanceIndex).ToArray();
        string Local(string name)
        {
            while (method.parameterNames.Any(parameter => parameter.TrimStart('@') == name)) name += "_";
            return name;
        }
        string call = Local("__bgcsCall"), resource = Local("__bgcsResource"), result = Local("__bgcsResult"), error = Local("__bgcsError"), status = Local("__bgcsStatus");
        string transferred = Local("__bgcsTransferred"), acquired = Local("__bgcsAcquired"), handle = Local("__bgcsHandle");
        string cleanup = "__BgcsReleaseOwned" + operation.methodIndex;
        string receiver = Local("__bgcsOwnedReceiverAddress");
        if (plan.methods.Any(candidate => candidate.name.TrimStart('@') == cleanup))
            throw new InvalidOperationException("An owned cleanup helper conflicts with a configured public method.");
        string[] arguments = method.parameterNames.ToArray();
        foreach (int input in inputs) arguments[input] = instance && ValueInputEmitter.IsAddress(method, input)
            ? "(" + method.parameterTypes[input] + ")" + arguments[input] : RecipeConversionEmitter.Input(method, input, arguments[input]);
        if (instance) arguments[operation.instanceIndex] = "(" + method.parameterTypes[operation.instanceIndex] + ")" + receiver;
        arguments[operation.errorIndex] = "&" + error;
        arguments[operation.resultIndex] = "&" + result;
        string errorType = method.parameterTypes[operation.errorIndex][..^1].TrimEnd();
        StringBuilder text = new();
        if (!instance)
            DocumentationProjector.AppendMethod(text, method.documentation, method.documentationContent, method.parameterNames,
                OverloadPlanner.PublicInputs(method, inputs), ResultType(plan, operation), raw: false, "    ");
        text.Append(instance ? "    private " : "    public ").Append(ResultType(plan, operation)).Append(' ').Append(instanceHelper ?? method.name).Append('(');
        if (instance)
            text.Append("nint ").Append(receiver).Append(string.Concat(TextInputEmitter.ForwardedInputs(method, inputs).Select(input => ", "
                + (ValueInputEmitter.IsAddress(method, input) ? "nint" : RecipeConversionEmitter.InputType(method, input)) + " " + method.parameterNames[input])));
        else text.Append(string.Join(", ", OverloadPlanner.PublicInputs(method, inputs).Select(index => OverloadPlanner.InputDeclaration(method, index))));
        text.AppendLine(")").AppendLine("    {");
        if (!instance) text.Append("        using var ").Append(call).AppendLine(" = this.m_session.AcquireCall();");
        TextInputEmitter.Append(text, method, request.runtimeNamespace, arguments, inputs);
        bool values = !instance && ValueInputEmitter.Open(text, method, arguments, inputs);
        bool buffers = !instance && BufferInputEmitter.Open(text, method, arguments, inputs);
        text.Append("        var ").Append(resource).AppendLine(" = this.m_session.AcquireResource();")
            .Append("        ").Append(operation.resultType).Append(' ').Append(result).AppendLine(" = null;")
            .Append("        ").Append(errorType).Append(' ').Append(error).AppendLine(" = null;")
            .Append("        bool ").Append(transferred).AppendLine(" = false;")
            .Append("        bool ").Append(acquired).AppendLine(" = false;")
            .AppendLine("        try").AppendLine("        {")
            .Append("            int ").Append(status).Append(" = ").Append(Invoke(operation.methodIndex)).Append('(').Append(string.Join(", ", arguments)).AppendLine(");")
            .Append("            if (").Append(status).Append(" != ").Append(operation.successValue).Append(") throw new ").Append(runtime)
                .Append(".NativeCallException(").Append(status).Append(", ").Append(ContextEmitter.Literal(method.exportName)).AppendLine(");");
        text.Append("            if (").Append(result).AppendLine(" == null) throw new global::System.InvalidOperationException(\"A successful owned native result was null.\");")
            .Append("            ").Append(acquired).AppendLine(" = true;");
        if (!instance) BufferInputEmitter.Complete(text, method, inputs, "            ");
        text.Append("            var ").Append(handle).Append(" = new ").Append(runtime).Append(".OwnedNativeHandle((nint)").Append(result).Append(", ").Append(resource)
                .Append(", this.").Append(cleanup).Append(", ").Append(ContextEmitter.Literal(owned.releaseDomain)).AppendLine(");")
            .Append("            ").Append(transferred).AppendLine(" = true;")
            .Append("            return ").Append(owned.objectName is null ? handle : "new " + owned.objectName + "(this, " + handle + ")").AppendLine(";")
            .AppendLine("        }").AppendLine("        finally").AppendLine("        {");
        text.AppendLine("            try").AppendLine("            {")
            .Append("                if (").Append(error).Append(" != null) ").Append(Invoke(operation.releaseIndex)).Append('(').Append(error).AppendLine(");")
            .AppendLine("            }").AppendLine("            finally").AppendLine("            {")
            .Append("                if (!").Append(transferred).AppendLine(")")
            .AppendLine("                {").AppendLine("                    try").AppendLine("                    {")
            .Append("                        if (").Append(acquired).Append(") this.").Append(cleanup).Append("((nint)").Append(result).AppendLine(");")
            .AppendLine("                    }").Append("                    finally { ").Append(resource).AppendLine(".Dispose(); }")
            .AppendLine("                }").AppendLine("            }").AppendLine("        }");
        BufferInputEmitter.Close(text, buffers);
        ValueInputEmitter.Close(text, values);
        text.AppendLine("    }").Append("    private void ").Append(cleanup).AppendLine("(nint address)").AppendLine("    {");
        string address = "(" + release.parameterTypes[0] + ")address";
        if (owned.releaseErrorIndex < 0)
            text.Append("        ").Append(Invoke(owned.releaseIndex)).Append('(').Append(address).AppendLine(");");
        else
        {
            string releaseErrorType = release.parameterTypes[owned.releaseErrorIndex][..^1].TrimEnd();
            text.Append("        ").Append(releaseErrorType).AppendLine(" error = null;")
                .AppendLine("        try").AppendLine("        {")
                .Append("            int status = ").Append(Invoke(owned.releaseIndex)).Append('(').Append(address).AppendLine(", &error);")
                .Append("            if (status != ").Append(owned.releaseSuccessValue).Append(") throw new ").Append(runtime)
                    .Append(".NativeCallException(status, ").Append(ContextEmitter.Literal(release.exportName)).AppendLine(");")
                .AppendLine("        }").AppendLine("        finally").AppendLine("        {")
                .Append("            if (error != null) ").Append(Invoke(owned.releaseErrorCleanupIndex)).AppendLine("(error);")
                .AppendLine("        }");
        }
        return text.AppendLine("    }").ToString();

        string Invoke(int index) => plan.importMode == BindingImportMode.FunctionTable ? "this.m_function" + index
            : "global::" + CSharpNames.Namespace(plan.api.@namespace) + "." + CSharpNames.Identifier(plan.contextName) + ".NativeImports.Function" + index;
    }
}
