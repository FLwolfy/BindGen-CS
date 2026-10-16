using System;
using System.Linq;
using System.Text;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

internal static class EventLoopAsyncOperationEmitter
{
    internal static string Emit(
        CSharpEmissionPlan plan,
        CSharpAsyncOperationPlan operation
    ) {
        CSharpMethodPlan method = plan.methods[operation.methodIndex];
        CSharpCallbackInputPlan callback = operation.callback;
        string runtime = "global::" + CSharpNames.Namespace(plan.runtimeNamespace), state = callback.stateName;
        string stop = "__BgcsAsyncStop_" + operation.methodIndex, cancel = "__BgcsAsyncCancel_" + operation.methodIndex;
        string Local(string suffix)
        {
            string name = "__bgcsOperation" + suffix;
            while (method.parameterNames.Any(parameter => parameter.TrimStart('@') == name)) name += "_";
            return name;
        }
        string localState = Local("State"), localModule = Local("Module"), localFailure = Local("Failure");
        StringBuilder text = new();
        text.Append("public sealed unsafe partial class ").Append(plan.contextName).AppendLine().AppendLine("{");
        DocumentationProjector.Append(text, method.documentation, "    ");
        text.Append("    public ").Append(operation.signature.returnType).Append(' ').Append(operation.signature.name).Append('(')
            .Append(string.Join(", ", Enumerable.Range(0, operation.signature.parameterNames.Length).Select(index => FacadeParameters.Declaration(operation.signature, index)))).AppendLine(")")
            .AppendLine("    {").Append("        using var ").Append(Local("Call")).AppendLine(" = m_session.AcquireCall();")
            .Append("        var ").Append(localState).Append(" = new ").Append(state).AppendLine("(this);")
            .Append("        ").Append(runtime).Append(".NativeModuleLease? ").Append(localModule).AppendLine(" = m_session.AcquireResource();")
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
            .Append(", ").Append(localState).Append(".StopAndJoinAsync, ").Append(localState).Append(".dispatcher, new ").Append(state).Append(".Resources(").Append(localState)
            .Append("), ").Append(localModule).Append(", ").Append(runtime).AppendLine(".CallbackShutdownOrder.KeepOpenUntilUnregistered);")
            .Append("            ").Append(localModule).AppendLine(" = null;");
        arguments[callback.parameterIndex] = "(" + method.parameterTypes[callback.parameterIndex] + ")" + RetainedCallbackEmitter.ThunkPointer(callback);
        arguments[callback.userDataIndex] = "(" + method.parameterTypes[callback.userDataIndex] + ")" + localState + ".token.token";
        text.AppendLine("            try").AppendLine("            {")
            .Append("                ").Append(localState).Append(".Publish((nint)").Append(RetainedCallbackEmitter.Invoke(plan, operation.methodIndex)).Append('(').Append(string.Join(", ", arguments)).AppendLine("), null);")
            .AppendLine("            }").Append("            catch (global::System.Exception ").Append(localFailure).Append(") { ").Append(localState).Append(".Publish(0, ").Append(localFailure).AppendLine("); throw; }")
            .Append("            if (").Append(localState).AppendLine(".nativeHandle == 0) throw new global::System.InvalidOperationException(\"Native async start returned null.\");")
            .Append("            return ").Append(localState).AppendLine(".operation;")
            .AppendLine("        }").AppendLine("        catch").AppendLine("        {")
            .Append("            if (").Append(localState).Append(".registration is not null) ").Append(localState).AppendLine(".registration.Dispose();")
            .Append("            else { ").Append(localState).Append(".token?.Dispose(); ").Append(localModule).Append("?.Dispose(); ").Append(localState).AppendLine(".ReleasePins(); }")
            .AppendLine("            throw;").AppendLine("        }").AppendLine("    }")
            .Append("    private void ").Append(stop).Append('(').Append(method.returnType).Append(" handle) => ").Append(RetainedCallbackEmitter.Invoke(plan, operation.unregisterIndex)).AppendLine("(handle);");
        if (operation.cancelIndex >= 0)
            text.Append("    private void ").Append(cancel).Append('(').Append(method.returnType).Append(" handle) => ").Append(RetainedCallbackEmitter.Invoke(plan, operation.cancelIndex)).AppendLine("(handle);");
        text.Append("    internal delegate void ").Append(callback.delegateName).Append('(')
            .Append(string.Join(", ", callback.publicIndices.Select(index => callback.parameterTypes[index] + " argument" + index))).AppendLine(");");
        string pinFields = string.Join("\n", operation.buffers.Select(buffer => "internal global::System.Buffers.MemoryHandle pin" + buffer.parameterIndex + "; internal bool hasPin" + buffer.parameterIndex + ";"));
        string releasePins = string.Join("\n", operation.buffers.Reverse().Select(buffer => "if (hasPin" + buffer.parameterIndex + ") { try { pin" + buffer.parameterIndex
            + ".Dispose(); } catch (global::System.Exception failure) { (failures ??= new()).Add(failure); } }"));
        string terminal = "if (failure is not null) operation.TrySetException(failure); else if (m_status == " + operation.successExpression + ") operation.TrySetResult("
            + (operation.booleanResult ? "m_result != 0" : "m_result") + ");"
            + (operation.cancellationExpression is { } cancelled ? " else if (m_status == " + cancelled + ") operation.TrySetCanceled();" : string.Empty)
            + " else operation.TrySetException(new global::System.InvalidOperationException(\"Native async completion reported failure status \" + global::System.Convert.ToString(m_status, global::System.Globalization.CultureInfo.InvariantCulture)));";
        string cancellation = operation.cancelIndex < 0 ? string.Empty : $$"""
            private void CancelReady()
            {
                dispatcher.VerifyAccess();
                {{runtime}}.CallbackRegistration.InvocationLease? invocation;
                lock (m_gate)
                {
                    if (m_stopping || !m_ready || nativeHandle == 0) return;
                    if (!registration!.TryEnterInvocation(out invocation)) return;
                    if (m_cancelCalls == 0) m_cancelDrained = new(global::System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
                    m_cancelCalls++;
                }
                try { m_context.{{cancel}}(({{method.returnType}})nativeHandle); }
                finally
                {
                    lock (m_gate) { if (--m_cancelCalls == 0) m_cancelDrained.TrySetResult(); }
                    invocation!.Dispose();
                }
            }
            """;
        string cancelRequest = operation.cancelIndex < 0 ? string.Empty
            : "_ = dispatcher.ContinueAsync(global::System.Threading.Tasks.ValueTask.CompletedTask, CancelReady, failure => exceptions.Capture(failure));";
        text.Append($$"""
                internal sealed class {{state}} : global::System.IDisposable
                {
                    private readonly object m_gate = new();
                    private readonly {{plan.contextName}} m_context;
                    private bool m_ready, m_signaled, m_stopping;
                    private int m_pinsReleased, m_cancelCalls = 0;
                    private global::System.Exception? m_creationFailure;
                    private {{callback.parameterTypes[operation.resultIndex]}} m_result;
                    private {{callback.parameterTypes[operation.statusIndex]}} m_status;
                    private readonly global::System.Threading.Tasks.TaskCompletionSource m_published = new(global::System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
                    private global::System.Threading.Tasks.TaskCompletionSource m_cancelDrained = CompletedDrain();
                    internal readonly {{runtime}}.NativeCallbackDispatcher dispatcher = {{runtime}}.NativeCallbackDispatcher.CaptureCurrent();
                    internal readonly {{callback.delegateName}} target;
                    internal readonly {{runtime}}.CallbackExceptionBoundary exceptions = new();
                    internal readonly {{runtime}}.NativeOperation<{{operation.resultType}}> operation;
                    internal {{runtime}}.CallbackRegistry<{{state}}>.Registration? token;
                    internal {{runtime}}.CallbackRegistration? registration;
                    internal nint nativeHandle;
                    {{pinFields}}
                    internal {{state}}({{plan.contextName}} context)
                    { m_context = context; target = Complete; operation = new(new global::System.IDisposable[] { this }, Cancel); }
                    private static global::System.Threading.Tasks.TaskCompletionSource CompletedDrain()
                    {
                        var ready = new global::System.Threading.Tasks.TaskCompletionSource(global::System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
                        ready.SetResult(); return ready;
                    }
                    internal void Publish(nint handle, global::System.Exception? failure)
                    {
                        lock (m_gate) { nativeHandle = handle; m_creationFailure = failure; m_ready = true; }
                        m_published.TrySetResult();
                    }
                    private void Complete({{string.Join(", ", callback.publicIndices.Select(index => callback.parameterTypes[index] + " argument" + index))}})
                    {
                        lock (m_gate)
                        {
                            if (m_signaled) return; m_signaled = true;
                            m_result = argument{{operation.resultIndex}}; m_status = argument{{operation.statusIndex}};
                        }
                        _ = dispatcher.ContinueAsync(registration!.DisposeAsync(), CompleteTerminal, failure => operation.TrySetException(failure));
                    }
                    private void CompleteTerminal()
                    {
                        var failure = exceptions.TakeException();
                        {{terminal}}
                    }
                    private void Cancel() { {{cancelRequest}} }
                    {{cancellation}}
                    internal global::System.Threading.Tasks.ValueTask StopAndJoinAsync()
                    {
                        global::System.Threading.Tasks.Task ready, drained;
                        lock (m_gate) { m_stopping = true; ready = m_published.Task; drained = m_cancelDrained.Task; }
                        return new(dispatcher.InvokeAfterAsync(global::System.Threading.Tasks.Task.WhenAll(ready, drained), StopReady));
                    }
                    private void StopReady()
                    {
                        dispatcher.VerifyAccess();
                        lock (m_gate)
                        {
                            if (!m_stopping || !m_ready || m_cancelCalls != 0) throw new global::System.InvalidOperationException("The event-loop native operation has not drained.");
                            if (m_creationFailure is not null) throw new global::System.InvalidOperationException("The native start did not establish a safely releasable handle.", m_creationFailure);
                        }
                        if (nativeHandle != 0) m_context.{{stop}}(({{method.returnType}})nativeHandle);
                    }
                    public void Dispose()
                    {
                        registration!.Dispose();
                        if (!registration.completion.IsCompletedSuccessfully) throw new global::System.InvalidOperationException("Native shutdown has not completed; operation resources remain retained.");
                    }
                    internal void ReleasePins()
                    {
                        if (global::System.Threading.Interlocked.Exchange(ref m_pinsReleased, 1) != 0) return;
                        global::System.Collections.Generic.List<global::System.Exception>? failures = null;
                        {{releasePins}}
                        if (failures is not null) throw new global::System.AggregateException("Native async pin cleanup failed.", failures);
                    }
                    internal sealed class Resources : global::System.IDisposable
                    {
                        private {{state}}? m_owner;
                        internal Resources({{state}} owner) => m_owner = owner;
                        public void Dispose()
                        {
                            var owner = m_owner; if (owner is null) return;
                            owner.ReleasePins(); owner.token?.Dispose(); m_owner = null;
                        }
                    }
                }
                """);
        RetainedCallbackEmitter.EmitThunk(text, callback, runtime);
        return text.AppendLine("}").ToString();
    }
}
