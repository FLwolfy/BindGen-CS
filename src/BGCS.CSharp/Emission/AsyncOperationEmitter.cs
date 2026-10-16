using System.Linq;
using System.Text;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

internal static class AsyncOperationEmitter
{
    internal static string Emit(
        CSharpEmissionPlan plan,
        CSharpAsyncOperationPlan operation
    ) {
        if (operation.nonBlockingShutdown) return EventLoopAsyncOperationEmitter.Emit(plan, operation);
        CSharpMethodPlan method = plan.methods[operation.methodIndex];
        CSharpCallbackInputPlan callback = operation.callback;
        string runtime = "global::" + CSharpNames.Namespace(plan.runtimeNamespace), state = callback.stateName;
        string handleType = method.returnType, stop = "__BgcsAsyncStop_" + operation.methodIndex, cancel = "__BgcsAsyncCancel_" + operation.methodIndex;
        string Local(string suffix)
        {
            string name = "__bgcsOperation" + suffix;
            while (method.parameterNames.Any(parameter => parameter.TrimStart('@') == name)) name += "_";
            return name;
        }
        string localState = Local("State"), localModule = Local("Module"), localFailure = Local("Failure"), localCleanup = Local("CleanupFailure");
        StringBuilder text = new();
        text.Append("public sealed unsafe partial class ").Append(plan.contextName).AppendLine().AppendLine("{");
        DocumentationProjector.Append(text, method.documentation, "    ");
        text.AppendLine("    /// <remarks>The operation retains each Memory buffer until native completion and full stop-and-join; cancellation is only a request. Do not reuse retained memory before its task completes.</remarks>")
            .Append("    public ").Append(operation.signature.returnType).Append(' ').Append(operation.signature.name).Append('(')
            .Append(string.Join(", ", Enumerable.Range(0, operation.signature.parameterNames.Length).Select(index => FacadeParameters.Declaration(operation.signature, index)))).AppendLine(")")
            .AppendLine("    {").Append("        using var ").Append(Local("Call")).AppendLine(" = m_session.AcquireCall();")
            .Append("        ").Append(runtime).Append(".NativeModuleLease? ").Append(localModule).AppendLine(" = m_session.AcquireResource();")
            .Append("        var ").Append(localState).Append(" = new ").Append(state).AppendLine("(this);")
            .AppendLine("        try").AppendLine("        {");
        string[] arguments = method.parameterNames.ToArray();
        foreach (CSharpAsyncBufferPlan buffer in operation.buffers)
        {
            string parameter = method.parameterNames[buffer.parameterIndex], pin = localState + ".pin" + buffer.parameterIndex;
            if (!buffer.allowsEmpty) text.Append("            if (").Append(parameter).AppendLine(".IsEmpty) throw new global::System.ArgumentException(\"The native operation requires a nonempty buffer.\");");
            text.Append("            ").Append(pin).Append(" = ").Append(parameter).AppendLine(".Pin();")
                .Append("            ").Append(localState).Append(".hasPin").Append(buffer.parameterIndex).AppendLine(" = true;")
                .Append("            if (").Append(pin).Append(".Pointer == null && !").Append(parameter).AppendLine(".IsEmpty) throw new global::System.ArgumentException(\"The memory manager returned a null address for nonempty native storage.\");")
                .Append("            if ((nuint)").Append(pin).Append(".Pointer % ").Append(buffer.requiredAlignment).AppendLine("U != 0) throw new global::System.ArgumentException(\"The pinned memory does not meet native alignment.\");");
            arguments[buffer.parameterIndex] = "(" + method.parameterTypes[buffer.parameterIndex] + ")" + pin + ".Pointer";
            foreach (int count in buffer.countIndices) arguments[count] = "checked((" + method.parameterTypes[count] + ")" + parameter + ".Length)";
        }
        text.Append("            ").Append(localState).Append(".token = ").Append(runtime).Append(".CallbackRegistry<").Append(state).Append(">.Register(").Append(localState).AppendLine(");")
            .Append("            ").Append(localState).Append(".registration = new ").Append(runtime).Append(".CallbackRegistration((nint)").Append(RetainedCallbackEmitter.ThunkPointer(callback))
            .Append(", ").Append(localState).Append(".StopAndJoin, ").Append(localState).Append(".token, ").Append(localModule).Append(", ").Append(runtime).AppendLine(".CallbackShutdownOrder.KeepOpenUntilUnregistered);")
            .Append("            ").Append(localModule).AppendLine(" = null;");
        arguments[callback.parameterIndex] = "(" + method.parameterTypes[callback.parameterIndex] + ")" + RetainedCallbackEmitter.ThunkPointer(callback);
        arguments[callback.userDataIndex] = "(" + method.parameterTypes[callback.userDataIndex] + ")" + localState + ".token.token";
        text.AppendLine("            try").AppendLine("            {")
            .Append("                ").Append(localState).Append(".Publish((nint)").Append(RetainedCallbackEmitter.Invoke(plan, operation.methodIndex))
            .Append('(').Append(string.Join(", ", arguments)).AppendLine("), null);")
            .AppendLine("            }").Append("            catch (global::System.Exception ").Append(localFailure).Append(") { ").Append(localState).Append(".Publish(0, ").Append(localFailure).AppendLine("); throw; }")
            .Append("            if (").Append(localState).AppendLine(".nativeHandle == 0) throw new global::System.InvalidOperationException(\"Native async start returned null.\");")
            .Append("            return ").Append(localState).AppendLine(".operation;")
            .AppendLine("        }").Append("        catch (global::System.Exception ").Append(localFailure).AppendLine(")").AppendLine("        {")
            .AppendLine("            try").AppendLine("            {")
            .Append("                if (").Append(localState).Append(".registration is not null) ").Append(localState).AppendLine(".registration.Dispose();")
            .Append("                else { ").Append(localState).Append(".token?.Dispose(); ").Append(localModule).AppendLine("?.Dispose(); }")
            .Append("                ").Append(localState).AppendLine(".ReleasePins();")
            .AppendLine("            }").Append("            catch (global::System.Exception ").Append(localCleanup).AppendLine(")")
            .Append("            { throw new global::System.AggregateException(\"Native async start and cleanup failed; native storage remains retained.\", ").Append(localFailure).Append(", ").Append(localCleanup).AppendLine("); }")
            .AppendLine("            throw;").AppendLine("        }").AppendLine("    }")
            .Append("    private void ").Append(stop).Append('(').Append(handleType).Append(" handle) => ")
            .Append(RetainedCallbackEmitter.Invoke(plan, operation.unregisterIndex)).AppendLine("(handle);");
        if (operation.cancelIndex >= 0)
            text.Append("    private void ").Append(cancel).Append('(').Append(handleType).Append(" handle) => ").Append(RetainedCallbackEmitter.Invoke(plan, operation.cancelIndex)).AppendLine("(handle);");
        text.Append("    internal delegate void ").Append(callback.delegateName).Append('(')
            .Append(string.Join(", ", callback.publicIndices.Select(index => callback.parameterTypes[index] + " argument" + index))).AppendLine(");")
            .Append("    internal sealed class ").Append(state).AppendLine(" : global::System.IDisposable")
            .AppendLine("    {").AppendLine("        private readonly object m_gate = new();")
            .Append("        private readonly ").Append(plan.contextName).AppendLine(" m_context;")
            .AppendLine("        private bool m_ready, m_signaled;").AppendLine("        private int m_pinsReleased;")
            .Append(operation.cancelIndex >= 0 ? "        private bool m_stopping;\n        private int m_cancelCalls;\n" : string.Empty)
            .AppendLine("        private global::System.Exception? m_creationFailure;")
            .Append("        private ").Append(callback.parameterTypes[operation.resultIndex]).AppendLine(" m_result;")
            .Append("        private ").Append(callback.parameterTypes[operation.statusIndex]).AppendLine(" m_status;")
            .Append("        internal readonly ").Append(callback.delegateName).AppendLine(" target;")
            .Append("        internal readonly ").Append(runtime).AppendLine(".CallbackExceptionBoundary exceptions = new();")
            .Append("        internal readonly ").Append(runtime).Append(".NativeOperation<").Append(operation.resultType).AppendLine("> operation;")
            .Append("        internal ").Append(runtime).Append(".CallbackRegistry<").Append(state).AppendLine(">.Registration? token;")
            .Append("        internal ").Append(runtime).AppendLine(".CallbackRegistration? registration;")
            .AppendLine("        internal nint nativeHandle;");
        foreach (CSharpAsyncBufferPlan buffer in operation.buffers)
            text.Append("        internal global::System.Buffers.MemoryHandle pin").Append(buffer.parameterIndex).AppendLine(";")
                .Append("        internal bool hasPin").Append(buffer.parameterIndex).AppendLine(";");
        text.Append("        internal ").Append(state).Append('(').Append(plan.contextName).AppendLine(" context)")
            .AppendLine("        {").AppendLine("            m_context = context; target = Complete;")
            .Append("            operation = new ").Append(runtime).Append(".NativeOperation<").Append(operation.resultType).AppendLine(">(new global::System.IDisposable[] { this }, Cancel);")
            .AppendLine("        }")
            .AppendLine("        internal void Publish(nint handle, global::System.Exception? failure)")
            .AppendLine("        {").AppendLine("            lock (m_gate) { nativeHandle = handle; m_creationFailure = failure; m_ready = true; global::System.Threading.Monitor.PulseAll(m_gate); }")
            .AppendLine("        }")
            .Append("        private void Complete(").Append(string.Join(", ", callback.publicIndices.Select(index => callback.parameterTypes[index] + " argument" + index))).AppendLine(")")
            .AppendLine("        {").AppendLine("            lock (m_gate)").AppendLine("            {")
            .AppendLine("                if (m_signaled) return; m_signaled = true;")
            .Append("                m_result = argument").Append(operation.resultIndex).Append("; m_status = argument").Append(operation.statusIndex).AppendLine(";")
            .AppendLine("            }").AppendLine("            _ = global::System.Threading.Tasks.Task.Run(Finish);").AppendLine("        }")
            .AppendLine("        private void Finish()").AppendLine("        {").AppendLine("            try").AppendLine("            {")
            .AppendLine("                registration!.Dispose(); // This executes outside the native completion frame and joins that entire frame.")
            .AppendLine("                var failure = exceptions.TakeException();")
            .AppendLine("                if (failure is not null) operation.TrySetException(failure);")
            .Append("                else if (m_status == ").Append(operation.successExpression).Append(") operation.TrySetResult(").Append(operation.booleanResult ? "m_result != 0" : "m_result").AppendLine(");");
        if (operation.cancellationExpression is { } cancelled)
            text.Append("                else if (m_status == ").Append(cancelled).AppendLine(") operation.TrySetCanceled();");
        text.AppendLine("                else operation.TrySetException(new global::System.InvalidOperationException(\"Native async completion reported failure status \" + global::System.Convert.ToString(m_status, global::System.Globalization.CultureInfo.InvariantCulture)));")
            .AppendLine("            }").AppendLine("            catch (global::System.Exception failure) { operation.TrySetException(failure); }")
            .AppendLine("        }")
            .AppendLine("        private void Cancel()").AppendLine("        {");
        if (operation.cancelIndex >= 0)
            text.Append("            ").Append(runtime).AppendLine(".CallbackRegistration.InvocationLease? invocation;")
                .AppendLine("            lock (m_gate)").AppendLine("            {")
                .AppendLine("                if (m_stopping || !m_ready || nativeHandle == 0) return;")
                .AppendLine("                if (!registration!.TryEnterInvocation(out invocation)) return; m_cancelCalls++;")
                .AppendLine("            }").Append("            try { m_context.").Append(cancel).Append("((").Append(handleType).AppendLine(")nativeHandle); }")
                .AppendLine("            finally").AppendLine("            {")
                .AppendLine("                lock (m_gate) { m_cancelCalls--; global::System.Threading.Monitor.PulseAll(m_gate); }")
                .AppendLine("                invocation!.Dispose();").AppendLine("            }");
        text.AppendLine("        }")
            .AppendLine("        internal void StopAndJoin()").AppendLine("        {")
            .AppendLine("            lock (m_gate)").AppendLine("            {")
            .Append(operation.cancelIndex >= 0 ? "                m_stopping = true;\n" : string.Empty)
            .AppendLine("                while (!m_ready" + (operation.cancelIndex >= 0 ? " || m_cancelCalls != 0" : string.Empty) + ") global::System.Threading.Monitor.Wait(m_gate);")
            .AppendLine("                if (m_creationFailure is not null) throw new global::System.InvalidOperationException(\"The native async start did not establish a safely releasable handle.\", m_creationFailure);")
            .AppendLine("            }").Append("            if (nativeHandle != 0) m_context.").Append(stop).Append("((").Append(handleType).AppendLine(")nativeHandle);")
            .AppendLine("        }")
            .AppendLine("        public void Dispose() { registration!.Dispose(); ReleasePins(); }")
            .AppendLine("        internal void ReleasePins()").AppendLine("        {")
            .AppendLine("            if (global::System.Threading.Interlocked.Exchange(ref m_pinsReleased, 1) != 0) return;")
            .AppendLine("            global::System.Collections.Generic.List<global::System.Exception>? failures = null;");
        foreach (CSharpAsyncBufferPlan buffer in operation.buffers.Reverse())
            text.Append("            if (hasPin").Append(buffer.parameterIndex).AppendLine(")")
                .AppendLine("            {").Append("                try { pin").Append(buffer.parameterIndex).AppendLine(".Dispose(); }")
                .AppendLine("                catch (global::System.Exception failure) { (failures ??= new()).Add(failure); }").AppendLine("            }");
        text.AppendLine("            if (failures is not null) throw new global::System.AggregateException(\"Async native storage pins failed to release.\", failures);")
            .AppendLine("        }").AppendLine("    }");
        RetainedCallbackEmitter.EmitThunk(text, callback, runtime);
        return text.AppendLine("}").ToString();
    }
}
