using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BGCS.Contracts;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Managed;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

internal static class OwnedObjectEmitter
{
    internal static string Emit(
        CSharpEmissionPlan plan,
        EmissionRequest request
    ) {
        string api = CSharpNames.Identifier(plan.contextName);
        string runtime = "global::" + CSharpNames.Namespace(request.runtimeNamespace);
        StringBuilder text = new();
        text.Append("public sealed unsafe partial class ").AppendLine(api).AppendLine("{");
        HashSet<int> helpers = [];
        foreach (CSharpObjectPlan item in plan.objects)
        {
            bool moduleBorrowed = item.kind == ManagedReceiverKind.ModuleBorrowed;
            if (!Enum.IsDefined(item.kind) || moduleBorrowed && !item.methodIndices.IsEmpty)
                throw new InvalidOperationException("A native object wrapper has an unsupported lifetime/member protocol.");
            string handleType = moduleBorrowed ? "ModuleBorrowedNativeHandle" : "OwnedNativeHandle";
            text.Append("    public sealed class ").Append(item.name).AppendLine(" : global::System.IDisposable")
                .AppendLine("    {").Append("        private readonly ").Append(api).AppendLine(" m_context;")
                .Append("        private readonly ").Append(runtime).Append('.').Append(handleType).AppendLine(" m_handle;")
                .Append("        internal ").Append(item.name).Append('(').Append(api).Append(" context, ").Append(runtime).Append('.').Append(handleType).AppendLine(" handle)")
                .AppendLine("        {").AppendLine("            m_context = context;").AppendLine("            m_handle = handle;").AppendLine("        }")
                .AppendLine("        public bool isClosed => m_handle.IsClosed;");
            if (!moduleBorrowed)
                text.AppendLine("        public global::System.Exception? releaseFailure => m_handle.releaseFailure;")
                    .AppendLine("        public string releaseDomain => m_handle.releaseDomain;");
            text.Append("        internal bool __BgcsBelongsTo(").Append(api).AppendLine(" context) => global::System.Object.ReferenceEquals(m_context, context);")
                .AppendLine("        public void Dispose() => m_handle.Dispose();")
                .AppendLine("        public TResult Invoke<TResult>(global::System.Func<nint, TResult> operation) => m_handle.Invoke(operation);");
            if (!moduleBorrowed)
                text.Append("        public ").Append(runtime).AppendLine(".BorrowedNativeHandle AsBorrowed() => m_handle.AsBorrowed();");
            foreach (int statusIndex in item.methodIndices)
            {
                CSharpStatusMethodPlan status = plan.statusMethods[statusIndex];
                CSharpMethodPlan method = plan.methods[status.methodIndex];
                string address = Local(method, "__bgcsAddress"), returned = Local(method, "__bgcsObjectResult");
                int[] inputs = status.inputIndices.Where(index => index != status.instanceIndex).ToArray();
                string[] helperArguments = method.parameterNames.ToArray();
                DocumentationProjector.AppendMethod(text, method.documentation, method.documentationContent, method.parameterNames,
                    OverloadPlanner.PublicInputs(method, inputs), OwnedResultEmitter.ResultType(plan, status), raw: false, "        ");
                text.Append("        public ").Append(OwnedResultEmitter.ResultType(plan, status)).Append(' ').Append(method.name).Append('(')
                    .Append(string.Join(", ", OverloadPlanner.PublicInputs(method, inputs).Select(index => OverloadPlanner.InputDeclaration(method, index)))).AppendLine(")")
                    .AppendLine("        {");
                bool values = ValueInputEmitter.Open(text, method, helperArguments, inputs, indent: "            ", captureAddresses: true);
                bool buffers = BufferInputEmitter.Open(text, method, helperArguments, inputs, indent: "            ", captureAddresses: true);
                string invocation = "m_context." + Helper(status.methodIndex) + "(" + string.Join(", ", new[] { address }.Concat(TextInputEmitter.ForwardedInputs(method, inputs).Select(index => helperArguments[index]))) + ")";
                text.Append("            ");
                if (status.resultType != "void") text.Append(OwnedResultEmitter.ResultType(plan, status)).Append(' ').Append(returned).Append(" = ");
                text.Append("m_handle.Invoke(").Append(address).Append(" => ");
                if (status.resultType == "void") text.Append("{ ").Append(invocation).Append("; return 0; }");
                else text.Append(invocation);
                text.AppendLine(");");
                if (status.ownedResult is not null && method.bufferProtocols.Any(protocol => inputs.Contains(protocol.parameterIndex)))
                {
                    string failure = Local(method, "__bgcsObjectValidationFailure");
                    text.AppendLine("            try").AppendLine("            {");
                    BufferInputEmitter.Complete(text, method, inputs, "                ");
                    text.AppendLine("            }").Append("            catch (global::System.Exception ").Append(failure).AppendLine(")")
                        .AppendLine("            {");
                    OwnedResultEmitter.AppendFailureCleanup(text, returned, failure, Local(method, "__bgcsObjectCleanupFailure"),
                        Local(method, "__bgcsObjectDisposeFailure"), "                ");
                    text.AppendLine("                throw;").AppendLine("            }");
                }
                else BufferInputEmitter.Complete(text, method, inputs, "            ");
                if (status.resultType != "void") text.Append("            return ").Append(returned).AppendLine(";");
                BufferInputEmitter.Close(text, buffers, indent: "            ");
                ValueInputEmitter.Close(text, values, indent: "            ");
                text.AppendLine("        }");
                helpers.Add(statusIndex);
            }
            foreach (CSharpReceiverMethodPlan member in item.directMethods) text.Append(DirectReceiverEmitter.EmitObjectMethod(plan, member));
            foreach (CSharpValueStringResultPlan member in plan.valueStringResults.Where(result => result.receiver?.objectName == item.name))
                text.Append(ValueStringReceiverEmitter.Emit(plan, member));
            foreach (CSharpCallbackMethodPlan callback in plan.callScopedCallbacks.Where(operation => operation.receiver?.objectName == item.name))
                text.Append(OwnedCallbackEmitter.Emit(plan, callback));
            text.AppendLine("    }");
        }
        foreach (int statusIndex in helpers.Order())
        {
            CSharpStatusMethodPlan status = plan.statusMethods[statusIndex];
            CSharpMethodPlan method = plan.methods[status.methodIndex];
            string helper = Helper(status.methodIndex);
            if (plan.methods.Any(candidate => candidate.name.TrimStart('@') == helper))
                throw new InvalidOperationException("An owned object dispatch helper conflicts with a public member.");
            if (status.ownedResult is not null)
            {
                text.Append(OwnedResultEmitter.EmitInstanceHelper(plan, request, status, helper));
                continue;
            }
            string address = Local(method, "__bgcsAddress"), error = Local(method, "__bgcsError"), result = Local(method, "__bgcsResult"), code = Local(method, "__bgcsStatus");
            int[] inputs = status.inputIndices.Where(index => index != status.instanceIndex).ToArray();
            string[] arguments = method.parameterNames.ToArray();
            foreach (int input in inputs) arguments[input] = ValueInputEmitter.IsAddress(method, input)
                ? "(" + method.parameterTypes[input] + ")" + arguments[input] : RecipeConversionEmitter.Input(method, input, arguments[input]);
            arguments[status.instanceIndex] = "(" + method.parameterTypes[status.instanceIndex] + ")" + address;
            arguments[status.errorIndex] = "&" + error;
            if (status.resultIndex >= 0) arguments[status.resultIndex] = "&" + result;
            text.Append("    private ").Append(RecipeConversionEmitter.ResultType(method, status)).Append(' ').Append(helper).Append("(nint ").Append(address);
            foreach (int input in TextInputEmitter.ForwardedInputs(method, inputs)) text.Append(", ").Append(ValueInputEmitter.IsAddress(method, input) ? "nint" : RecipeConversionEmitter.InputType(method, input)).Append(' ').Append(method.parameterNames[input]);
            text.AppendLine(")").AppendLine("    {")
                .Append("        ").Append(method.parameterTypes[status.errorIndex][..^1].TrimEnd()).Append(' ').Append(error).AppendLine(" = null;");
            if (status.resultIndex >= 0) text.Append("        ").Append(status.resultType).Append(' ').Append(result).AppendLine(" = default;");
            TextInputEmitter.Append(text, method, request.runtimeNamespace, arguments, inputs);
            text.AppendLine("        try").AppendLine("        {")
                .Append("            int ").Append(code).Append(" = ").Append(Invoke(status.methodIndex)).Append('(').Append(string.Join(", ", arguments)).AppendLine(");")
                .Append("            if (").Append(code).Append(" != ").Append(status.successValue).Append(") throw new ").Append(runtime)
                    .Append(".NativeCallException(").Append(code).Append(", ").Append(ContextEmitter.Literal(method.exportName)).AppendLine(");");
            if (status.resultIndex >= 0) text.Append("            return ").Append(RecipeConversionEmitter.Result(method, status.resultIndex, result)).AppendLine(";");
            text.AppendLine("        }").AppendLine("        finally").AppendLine("        {")
                .Append("            if (").Append(error).Append(" != null) ").Append(Invoke(status.releaseIndex)).Append('(').Append(error).AppendLine(");")
                .AppendLine("        }").AppendLine("    }");
        }
        foreach (CSharpReceiverMethodPlan member in plan.objects.SelectMany(item => item.directMethods)) text.Append(DirectReceiverEmitter.EmitHelper(plan, member));
        foreach (CSharpValueStringResultPlan member in plan.valueStringResults.Where(result => result.receiver is not null))
            text.Append(ValueStringResultEmitter.EmitInstanceHelper(plan, member));
        return text.AppendLine("}").ToString();

        string Invoke(int index) => plan.importMode == BindingImportMode.FunctionTable ? "this.m_function" + index
            : "global::" + CSharpNames.Namespace(plan.api.@namespace) + "." + api + ".NativeImports.Function" + index;
    }

    internal static string Helper(int index) => "__BgcsObjectCall" + index;
    private static string Local(
        CSharpMethodPlan method,
        string name
    ) {
        while (method.parameterNames.Any(parameter => parameter.TrimStart('@') == name)) name += "_";
        return name;
    }
}
