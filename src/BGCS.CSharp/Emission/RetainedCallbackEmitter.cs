using System.Linq;
using System.Text;
using BGCS.Core;
using BGCS.Core.Interop;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

internal static class RetainedCallbackEmitter
{
    internal static string Emit(
        CSharpEmissionPlan plan,
        CSharpRetainedCallbackPlan operation
    ) {
        CSharpMethodPlan method = plan.methods[operation.methodIndex];
        CSharpCallbackInputPlan callback = operation.callback;
        string runtime = "global::" + CSharpNames.Namespace(plan.runtimeNamespace);
        string state = callback.stateName, registration = operation.registrationName;
        string stop = "__BgcsStop_" + operation.methodIndex;
        bool paired = operation.kind == NativeCallbackRegistrationKind.CallbackAndUserData;
        StringBuilder text = new();
        text.Append("public sealed unsafe partial class ").Append(plan.contextName).AppendLine().AppendLine("{")
            .AppendLine("    /// <summary>Receives borrowed callback arguments while the declared native registration is active.</summary>")
            .Append("    public delegate ").Append(callback.resultType).Append(' ').Append(callback.delegateName).Append('(')
            .Append(string.Join(", ", callback.publicIndices.Select(index => callback.parameterTypes[index] + " argument" + index))).AppendLine(");")
            .AppendLine(paired ? "    /// <summary>Borrows this registration's opaque user-data identity for one synchronous operation.</summary>"
                : "    /// <summary>Borrows a native registration handle for one synchronous operation; native retention is governed by stop-and-join.</summary>")
            .Append("    public delegate TResult ").Append(operation.nativeCallName).Append("<TResult>(").Append(operation.handleType).AppendLine(" handle);")
            .AppendLine("    /// <summary>Retains callback state and the native module until the declared full native stop-and-join operation completes.</summary>")
            .Append("    public sealed class ").Append(registration).AppendLine(" : global::System.IDisposable, global::System.IAsyncDisposable")
            .AppendLine("    {").Append("        private readonly ").Append(state).AppendLine(" m_state;")
            .Append("        internal ").Append(registration).Append('(').Append(state).AppendLine(" state) => m_state = state;")
            .AppendLine("        public bool isClosing => m_state.registration!.isClosing;")
            .AppendLine("        public global::System.Exception? closeFailure => m_state.registration!.closeFailure;")
            .AppendLine("        public global::System.Threading.Tasks.Task completion => m_state.registration!.completion;")
            .AppendLine("        public global::System.Exception? TakeCallbackException() => m_state.exceptions.TakeException();")
            .AppendLine("        public void Dispose() => m_state.registration!.Dispose();")
            .AppendLine("        /// <summary>Runs native shutdown on an allowed cleanup thread. Await completion outside this registration's callbacks.</summary>")
            .AppendLine(operation.nonBlockingShutdown ? "        public global::System.Threading.Tasks.ValueTask DisposeAsync() => m_state.registration!.DisposeAsync();"
                : "        public global::System.Threading.Tasks.ValueTask DisposeAsync() => new(global::System.Threading.Tasks.Task.Run(() => m_state.registration!.Dispose()));")
            .Append("        public TResult Invoke<TResult>(").Append(operation.nativeCallName).AppendLine("<TResult> operation)")
            .AppendLine("        {").AppendLine("            global::System.ArgumentNullException.ThrowIfNull(operation);")
            .AppendLine("            using var use = m_state.EnterNativeUse();")
            .Append("            return operation((").Append(operation.handleType).AppendLine(")m_state.nativeHandle);")
            .AppendLine("        }")
            .AppendLine("        /// <summary>Exposes an unsafe borrowed handle; every external use must be synchronized with the declared native stop-and-join protocol.</summary>")
            .Append("        public ").Append(operation.handleType).AppendLine(paired ? " DangerousGetNativeUserData()" : " DangerousGetNativeHandle()")
            .AppendLine("        {").AppendLine("            global::System.ObjectDisposedException.ThrowIf(m_state.isPublicUseClosed, this);")
            .Append("            return (").Append(operation.handleType).AppendLine(")m_state.nativeHandle;")
            .AppendLine("        }").AppendLine("    }");
        string Local(string suffix)
        {
            string name = "__bgcsRegistration" + suffix;
            while (method.parameterNames.Any(parameter => parameter.TrimStart('@') == name)) name += "_";
            return name;
        }
        string factoryState = Local("State"), factoryModule = Local("Module"), factoryCall = Local("Call"),
            factoryFailure = Local("Failure"), factoryCleanupFailure = Local("CleanupFailure");
        DocumentationProjector.AppendMethod(text, method.documentation, method.documentationContent, method.parameterNames,
            operation.signature.inputIndices, operation.signature.returnType, raw: false, "    ");
        text.Append("    public ").Append(registration).Append(' ').Append(method.name).Append('(')
            .Append(string.Join(", ", Enumerable.Range(0, operation.signature.parameterNames.Length).Select(index => FacadeParameters.Declaration(operation.signature, index)))).AppendLine(")")
            .AppendLine("    {").Append("        global::System.ArgumentNullException.ThrowIfNull(").Append(method.parameterNames[callback.parameterIndex]).AppendLine(");")
            .AppendLine("        using var " + factoryCall + " = m_session.AcquireCall();")
            .Append("        var " + factoryState + " = new ").Append(state).Append("(this, ").Append(method.parameterNames[callback.parameterIndex]).AppendLine(");")
            .Append("        ").Append(runtime).AppendLine(".NativeModuleLease? " + factoryModule + " = m_session.AcquireResource();")
            .AppendLine("        try").AppendLine("        {")
            .Append("            " + factoryState + ".token = ").Append(runtime).Append(".CallbackRegistry<").Append(state).AppendLine(">.Register(" + factoryState + ");")
            .Append("            " + factoryState + ".registration = new ").Append(runtime).Append(".CallbackRegistration(" + factoryState + ".callbackPointer")
            .Append(", " + factoryState + (operation.nonBlockingShutdown ? ".StopAndJoinAsync, " + factoryState + ".dispatcher, " : ".StopAndJoin, ") + factoryState + ", " + factoryModule + ", ").Append(runtime).AppendLine(".CallbackShutdownOrder.KeepOpenUntilUnregistered);")
            .AppendLine("            " + factoryModule + " = null;");
        string[] arguments = method.parameterNames.ToArray();
        arguments[callback.parameterIndex] = "(" + method.parameterTypes[callback.parameterIndex] + ")" + factoryState + ".callbackPointer";
        arguments[callback.userDataIndex] = "(" + method.parameterTypes[callback.userDataIndex] + ")" + factoryState + ".token.token";
        text.AppendLine("            try").AppendLine("            {");
        string native = Invoke(plan, operation.methodIndex) + "(" + string.Join(", ", arguments) + ")";
        if (!paired) text.Append("                ").Append(factoryState).Append(".Publish((nint)").Append(native).AppendLine(", null);");
        else if (method.returnType == "void")
            text.Append("                ").Append(native).AppendLine(";").Append("                ").Append(factoryState).Append(".Publish(").Append(factoryState).AppendLine(".token.token, null);");
        else
            text.Append("                ").Append(method.returnType).Append(' ').Append(Local("Status")).Append(" = ").Append(native).AppendLine(";")
                .Append("                ").Append(factoryState).Append(".Publish(").Append(Local("Status")).Append(" == ").Append(operation.successExpression)
                .Append(" ? ").Append(factoryState).AppendLine(".token.token : 0, null);");
        text.AppendLine("            }").AppendLine("            catch (global::System.Exception " + factoryFailure + ") { " + factoryState + ".Publish(0, " + factoryFailure + "); throw; }")
            .AppendLine("            if (" + factoryState + ".nativeHandle == 0) throw new global::System.InvalidOperationException(\"Native callback registration did not establish an active registration.\");")
            .Append("            return new ").Append(registration).AppendLine("(" + factoryState + ");")
            .AppendLine("        }").AppendLine("        catch (global::System.Exception " + factoryFailure + ")").AppendLine("        {")
            .AppendLine("            try").AppendLine("            {")
            .AppendLine("                if (" + factoryState + ".registration is not null) " + factoryState + ".registration.Dispose();")
            .AppendLine("                else { " + factoryState + ".token?.Dispose(); " + factoryModule + "?.Dispose(); }")
            .AppendLine("            }").AppendLine("            catch (global::System.Exception " + factoryCleanupFailure + ")")
            .AppendLine("            { throw new global::System.AggregateException(\"Native callback registration and cleanup failed; native resources remain retained.\", " + factoryFailure + ", " + factoryCleanupFailure + "); }")
            .AppendLine("            throw;").AppendLine("        }").AppendLine("    }");
        string[] unregisterArguments = ["handle"];
        if (paired)
        {
            CSharpMethodPlan unregister = plan.methods[operation.unregisterIndex];
            unregisterArguments = new string[2];
            unregisterArguments[operation.unregisterCallbackIndex] = "(" + unregister.parameterTypes[operation.unregisterCallbackIndex] + ")callbackPointer";
            unregisterArguments[operation.unregisterUserDataIndex] = "(" + unregister.parameterTypes[operation.unregisterUserDataIndex] + ")handle";
        }
        text.Append("    private void ").Append(stop).Append('(').Append(operation.handleType).Append(paired ? " handle, nint callbackPointer) => " : " handle) => ")
            .Append(Invoke(plan, operation.unregisterIndex)).Append('(').Append(string.Join(", ", unregisterArguments)).AppendLine(");");
        if (operation.nonBlockingShutdown) text.Append(EventLoopCallbackStateEmitter.Emit(plan, operation, runtime, stop));
        else text.Append("    internal sealed class ").Append(state).AppendLine(" : global::System.IDisposable")
            .AppendLine("    {").AppendLine("        private readonly object m_gate = new();")
            .Append("        private readonly ").Append(plan.contextName).AppendLine(" m_context;")
            .AppendLine("        private bool m_ready;").AppendLine("        private bool m_stopping;").AppendLine("        private int m_nativeUses;")
            .AppendLine("        private global::System.Exception? m_creationFailure;")
            .AppendLine("        internal readonly nint callbackPointer;")
            .Append("        internal readonly ").Append(callback.delegateName).AppendLine(" target;")
            .Append("        internal readonly ").Append(runtime).AppendLine(".CallbackExceptionBoundary exceptions = new();")
            .Append("        internal ").Append(runtime).Append(".CallbackRegistry<").Append(state).AppendLine(">.Registration? token;")
            .Append("        internal ").Append(runtime).AppendLine(".CallbackRegistration? registration;")
            .AppendLine("        internal nint nativeHandle;")
            .AppendLine("        internal bool isPublicUseClosed => m_context.m_session.isClosing || registration!.isClosing;")
            .Append("        internal ").Append(state).Append('(').Append(plan.contextName).Append(" context, ").Append(callback.delegateName)
            .Append(" target) { m_context = context; this.target = target; callbackPointer = (nint)").Append(ThunkPointer(callback)).AppendLine("; }")
            .AppendLine("        internal void Publish(nint handle, global::System.Exception? failure)")
            .AppendLine("        {").AppendLine("            lock (m_gate) { nativeHandle = handle; m_creationFailure = failure; m_ready = true; global::System.Threading.Monitor.PulseAll(m_gate); }")
            .AppendLine("        }")
            .AppendLine("        internal NativeUse EnterNativeUse()")
            .AppendLine("        {").AppendLine("            lock (m_gate)").AppendLine("            {")
            .AppendLine("                global::System.ObjectDisposedException.ThrowIf(m_stopping || isPublicUseClosed, this);")
            .AppendLine("                if (!registration!.TryEnterInvocation(out var invocation)) throw new global::System.ObjectDisposedException(GetType().Name);")
            .AppendLine("                m_nativeUses++; return new NativeUse(this, invocation!);")
            .AppendLine("            }").AppendLine("        }")
            .AppendLine("        internal void StopAndJoin()")
            .AppendLine("        {").AppendLine("            lock (m_gate)").AppendLine("            {")
            .AppendLine("                m_stopping = true;")
            .AppendLine("                while (!m_ready || m_nativeUses != 0) global::System.Threading.Monitor.Wait(m_gate);")
            .AppendLine("                if (m_creationFailure is not null) throw new global::System.InvalidOperationException(\"The native registration call did not establish a safely releasable handle.\", m_creationFailure);")
            .AppendLine("            }").Append("            if (nativeHandle != 0) m_context.").Append(stop).Append("((").Append(operation.handleType).Append(")nativeHandle")
            .AppendLine(paired ? ", callbackPointer);" : ");")
            .AppendLine("        }")
            .AppendLine("        public void Dispose() => token?.Dispose();")
            .AppendLine("        internal sealed class NativeUse : global::System.IDisposable")
            .AppendLine("        {").Append("            private ").Append(state).AppendLine("? m_owner;")
            .Append("            private readonly ").Append(runtime).AppendLine(".CallbackRegistration.InvocationLease m_invocation;")
            .Append("            internal NativeUse(").Append(state).Append(" owner, ").Append(runtime).AppendLine(".CallbackRegistration.InvocationLease invocation) { m_owner = owner; m_invocation = invocation; }")
            .AppendLine("            public void Dispose()").AppendLine("            {")
            .AppendLine("                var owner = global::System.Threading.Interlocked.Exchange(ref m_owner, null); if (owner is null) return;")
            .AppendLine("                lock (owner.m_gate) { owner.m_nativeUses--; global::System.Threading.Monitor.PulseAll(owner.m_gate); }")
            .AppendLine("                m_invocation.Dispose();").AppendLine("            }").AppendLine("        }").AppendLine("    }");
        EmitThunk(text, callback, runtime);
        return text.AppendLine("}").ToString();
    }

    internal static void EmitThunk(
        StringBuilder text,
        CSharpCallbackInputPlan callback,
        string runtime
    ) {
        text.Append("    [global::System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new global::System.Type[] { typeof(global::System.Runtime.CompilerServices.CallConv")
            .Append(callback.callingConvention).AppendLine(") })]")
            .Append("    private static ").Append(callback.resultType).Append(' ').Append(callback.thunkName).Append('(')
            .Append(string.Join(", ", callback.parameterTypes.Select((
                type,
                index
            ) => type + " argument" + index))).AppendLine(")")
            .AppendLine("    {").Append("        ").Append(callback.stateName).AppendLine("? state = null;")
            .Append("        ").Append(runtime).AppendLine(".CallbackRegistration.InvocationLease? invocation = null;")
            .AppendLine("        try").AppendLine("        {")
            .Append("            if (!").Append(runtime).Append(".CallbackRegistry<").Append(callback.stateName).Append(">.TryGet((nint)argument")
            .Append(callback.callableUserDataIndex).AppendLine(", out state) || state.registration is null || !state.registration.TryEnterInvocation(out invocation))")
            .Append("                ").AppendLine(Fallback(callback));
        text.Append("            ");
        if (callback.resultType != "void") text.Append("return ");
        text.Append("state.target(").Append(string.Join(", ", callback.publicIndices.Select(index => "argument" + index))).AppendLine(");")
            .AppendLine("        }").AppendLine("        catch (global::System.Exception failure)").AppendLine("        {")
            .AppendLine("            try { state?.exceptions.Capture(failure); } catch { }")
            .Append("            ").AppendLine(Fallback(callback)).AppendLine("        }")
            .AppendLine("        finally").AppendLine("        {")
            .AppendLine("            try { invocation?.Dispose(); } catch (global::System.Exception failure) { try { state?.exceptions.Capture(failure); } catch { } }")
            .AppendLine("        }").AppendLine("    }");
    }

    internal static string ThunkPointer(CSharpCallbackInputPlan callback) => "(delegate* unmanaged[" + callback.callingConvention + "]<"
        + string.Join(", ", callback.parameterTypes.Append(callback.resultType)) + ">)&" + callback.thunkName;
    internal static string Invoke(
        CSharpEmissionPlan plan,
        int methodIndex
    ) => plan.importMode == BindingImportMode.FunctionTable
        ? "this.m_function" + methodIndex : "global::" + CSharpNames.Namespace(plan.api.@namespace) + "." + plan.contextName + ".NativeImports.Function" + methodIndex;
    private static string Fallback(CSharpCallbackInputPlan callback) => callback.resultType == "void" ? "return;" : "return " + callback.fallbackExpression + ";";
}
