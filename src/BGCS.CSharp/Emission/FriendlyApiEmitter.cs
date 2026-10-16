using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BGCS.Contracts;
using BGCS.Contracts.Requests;
using BGCS.CSharp.Planning;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Common;
using BGCS.Contracts.Managed;

namespace BGCS.CSharp.Emission;

internal static class FriendlyApiEmitter
{
    internal static string Emit(
        CSharpEmissionPlan plan,
        EmissionRequest request
    ) {
        Dictionary<StableId, InteropFunction> nativeExports = plan.api.interop.functions.ToDictionary(function => function.id);
        Dictionary<StableId, ManagedMember> members = plan.api.methods.ToDictionary(member => member.nativeExport);
        StringBuilder text = new();
        text.Append("public sealed unsafe partial class ").AppendLine(CSharpNames.Identifier(plan.contextName)).AppendLine("{");
        foreach (CSharpMethodPlan method in plan.methods)
        {
            if (!nativeExports.TryGetValue(method.nativeExport, out InteropFunction? function) || !members.TryGetValue(method.nativeExport, out ManagedMember? member))
                throw new InvalidOperationException("A friendly method lost its native export or managed member identity.");
            if (!OverloadPlanner.IsDirect(plan, method, function)) continue;
            if (!method.bufferInputs.IsDefaultOrEmpty || !method.valueParameters.IsDefaultOrEmpty || method.stringInputs.Any(input => input.lengthIndex is not null))
            {
                text.Append(SpanMethodEmitter.Emit(method, request.runtimeNamespace));
                continue;
            }
            DocumentationProjector.AppendMethod(text, method.documentation, method.documentationContent, method.parameterNames,
                Enumerable.Range(0, method.parameterNames.Length), RecipeConversionEmitter.DirectResultType(method), raw: false, "    ");
            text.Append("    public ").Append(RecipeConversionEmitter.DirectResultType(method)).Append(' ').Append(method.name).Append('(')
                .Append(string.Join(", ", method.parameterTypes.Select((
                    type,
                    index
                ) => (member.stringInputs.Contains(function.parameters[index].id)
                    ? function.parameters[index].text!.allowsNull == true ? "string?" : "string" : RecipeConversionEmitter.InputType(method, index)) + " " + method.parameterNames[index]))).AppendLine(")")
                .AppendLine("    {");
            string[] arguments = method.parameterNames.ToArray();
            for (int index = 0; index < arguments.Length; index++) arguments[index] = RecipeConversionEmitter.Input(method, index, arguments[index]);
            TextInputEmitter.Append(text, method, request.runtimeNamespace, arguments);
            text.Append("        ");
            if (method.returnType != "void") text.Append("return ");
            text.Append(RecipeConversionEmitter.DirectResult(method, "this.Raw." + method.name + "(" + string.Join(", ", arguments) + ")")).AppendLine(";").AppendLine("    }");
        }
        foreach (CSharpNullDefaultMethodPlan operation in plan.nullDefaultMethods)
            text.Append(NullDefaultMethodEmitter.Emit(plan, operation));
        foreach (CSharpDirectOwnedFactoryPlan factory in plan.directOwnedFactories)
            text.Append(DirectOwnedFactoryEmitter.Emit(plan, factory));
        foreach (CSharpModuleBorrowedFactoryPlan factory in plan.directBorrowedFactories)
            text.Append(ModuleBorrowedFactoryEmitter.Emit(plan, factory));
        foreach (CSharpStringResultPlan result in plan.stringResults)
            text.Append(StringResultEmitter.Emit(plan, result));
        foreach (CSharpValueStringResultPlan result in plan.valueStringResults)
            text.Append(ValueStringResultEmitter.Emit(plan, result));
        foreach (CSharpReceiverMethodPlan receiver in plan.objects.SelectMany(item => item.directMethods))
            text.Append(DirectReceiverEmitter.EmitContext(plan, receiver));
        foreach (CSharpStatusMethodPlan statusPlan in plan.statusMethods)
        {
            if (statusPlan.instanceIndex >= 0) continue;
            CSharpMethodPlan method = plan.methods[statusPlan.methodIndex];
            if (statusPlan.ownedResult is not null)
            {
                text.Append(OwnedResultEmitter.Emit(plan, request, statusPlan));
                continue;
            }
            string Local(string name)
            {
                while (method.parameterNames.Any(parameter => parameter.TrimStart('@') == name)) name += "_";
                return name;
            }
            string call = Local("__bgcsCall");
            string error = Local("__bgcsError");
            string result = Local("__bgcsResult");
            string status = Local("__bgcsStatus");
            string errorType = method.parameterTypes[statusPlan.errorIndex][..^1].TrimEnd();
            DocumentationProjector.AppendMethod(text, method.documentation, method.documentationContent, method.parameterNames,
                OverloadPlanner.PublicInputs(method, statusPlan.inputIndices), RecipeConversionEmitter.ResultType(method, statusPlan), raw: false, "    ");
            text.Append("    public ").Append(RecipeConversionEmitter.ResultType(method, statusPlan)).Append(' ').Append(method.name).Append('(')
                .Append(string.Join(", ", OverloadPlanner.PublicInputs(method, statusPlan.inputIndices).Select(index => OverloadPlanner.InputDeclaration(method, index)))).AppendLine(")")
                .AppendLine("    {").Append("        using var ").Append(call).AppendLine(" = this.m_session.AcquireCall();")
                .Append("        ").Append(errorType).Append(' ').Append(error).AppendLine(" = null;");
            if (statusPlan.resultIndex >= 0) text.Append("        ").Append(statusPlan.resultType).Append(' ').Append(result).AppendLine(" = default;");
            string[] arguments = method.parameterNames.ToArray();
            foreach (int input in statusPlan.inputIndices) arguments[input] = RecipeConversionEmitter.Input(method, input, arguments[input]);
            arguments[statusPlan.errorIndex] = "&" + error;
            if (statusPlan.resultIndex >= 0) arguments[statusPlan.resultIndex] = "&" + result;
            TextInputEmitter.Append(text, method, request.runtimeNamespace, arguments, statusPlan.inputIndices);
            bool values = ValueInputEmitter.Open(text, method, arguments, statusPlan.inputIndices);
            bool buffers = BufferInputEmitter.Open(text, method, arguments, statusPlan.inputIndices);
            text.AppendLine("        try").AppendLine("        {")
                .Append("            int ").Append(status).Append(" = ").Append(Invoke(statusPlan.methodIndex)).Append('(').Append(string.Join(", ", arguments)).AppendLine(");")
                .Append("            if (").Append(status).Append(" != ").Append(statusPlan.successValue).Append(") throw new global::").Append(CSharpNames.Namespace(request.runtimeNamespace))
                .Append(".NativeCallException(").Append(status).Append(", ").Append(ContextEmitter.Literal(method.exportName)).AppendLine(");");
            BufferInputEmitter.Complete(text, method, statusPlan.inputIndices, "            ");
            if (statusPlan.resultIndex >= 0) text.Append("            return ").Append(RecipeConversionEmitter.Result(method, statusPlan.resultIndex, result)).AppendLine(";");
            text.AppendLine("        }").AppendLine("        finally").AppendLine("        {");
            text.Append("            if (").Append(error).Append(" != null) ").Append(Invoke(statusPlan.releaseIndex)).Append('(').Append(error).AppendLine(");")
                .AppendLine("        }");
            BufferInputEmitter.Close(text, buffers);
            ValueInputEmitter.Close(text, values);
            text.AppendLine("    }");
        }
        return text.AppendLine("}").ToString();

        string Invoke(int index) => plan.importMode == BindingImportMode.FunctionTable ? "this.m_function" + index : "global::" + CSharpNames.Namespace(plan.api.@namespace) + "." + CSharpNames.Identifier(plan.contextName) + ".NativeImports.Function" + index;

    }
}
