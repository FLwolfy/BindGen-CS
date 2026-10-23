using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BGCS.Core;
using BGCS.Core.Requests;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

internal static class VirtualProxyEmitter
{
    internal static string Emit(
        CSharpEmissionPlan plan,
        CSharpVirtualProxyPlan proxy,
        EmissionRequest request
    )
        => Emit(plan, proxy, request.runtimeNamespace);

    internal static string Emit(
        CSharpEmissionPlan plan,
        CSharpVirtualProxyPlan proxy,
        string runtimeNamespace
    ) {
        string context = plan.contextName, runtime = "global::" + CSharpNames.Namespace(runtimeNamespace);
        string state = "__Bgcs" + proxy.name + "CallbackState", createHelper = Helper(proxy, "Create"), destroyHelper = Helper(proxy, "Destroy"), castHelper = Helper(proxy, "Cast");
        string nativeCall = proxy.name + "NativeCall";
        StringBuilder text = new();
        text.Append("public sealed unsafe partial class ").Append(context).AppendLine().AppendLine("{")
            .AppendLine("    /// <summary>Implements the selected native virtual methods; address arguments are borrowed only for each callback.</summary>")
            .Append("    public interface ").Append(proxy.interfaceName).AppendLine().AppendLine("    {");
        foreach (CSharpVirtualSlotPlan slot in proxy.slots)
            text.Append("        ").Append(slot.result?.managedType ?? "void").Append(' ').Append(slot.name).Append('(')
                .Append(string.Join(", ", slot.parameters.Select((
                    parameter,
                    index
                ) => parameter.managedType + " argument" + index))).AppendLine(");");
        text.AppendLine("    }")
            .AppendLine("    /// <summary>Uses a borrowed native interface while its registration and module are retained.</summary>")
            .Append("    public delegate TResult ").Append(nativeCall).Append("<TResult>(").Append(proxy.interfacePointerType).AppendLine(" nativeInterface);")
            .AppendLine("    /// <summary>Owns generated thunks and the proxy; native stop-and-join must finish before destruction.</summary>")
            .Append("    public sealed class ").Append(proxy.registrationName).AppendLine(" : global::System.IDisposable, global::System.IAsyncDisposable")
            .AppendLine("    {").Append("        private readonly ").Append(state).AppendLine(" m_state;")
            .Append("        internal ").Append(proxy.registrationName).Append('(').Append(state).AppendLine(" state) => m_state = state;")
            .AppendLine("        public bool isClosing => m_state.registration!.isClosing;")
            .AppendLine("        public global::System.Exception? closeFailure => m_state.registration!.closeFailure;")
            .AppendLine("        public global::System.Threading.Tasks.Task completion => m_state.registration!.completion;")
            .AppendLine("        public global::System.Exception? TakeCallbackException() => m_state.exceptions.TakeException();")
            .AppendLine("        public void Dispose() => m_state.registration!.Dispose();")
            .AppendLine("        public global::System.Threading.Tasks.ValueTask DisposeAsync() => m_state.registration!.DisposeAsync();")
            .AppendLine("        /// <summary>Retains this proxy for a synchronous native operation. Native retention ends only through the supplied stop-and-join protocol.</summary>")
            .Append("        public TResult Invoke<TResult>(").Append(nativeCall).AppendLine("<TResult> operation)")
            .AppendLine("        {").AppendLine("            global::System.ArgumentNullException.ThrowIfNull(operation);")
            .AppendLine("            using var use = m_state.EnterNativeUse();")
            .Append("            return operation((").Append(proxy.interfacePointerType).AppendLine(")m_state.nativeInterface);")
            .AppendLine("        }")
            .AppendLine("        /// <summary>Borrows the native interface without a call lease. Synchronize external uses with the supplied native stop-and-join action.</summary>")
            .Append("        public ").Append(proxy.interfacePointerType).AppendLine(" DangerousGetNativeInterface()")
            .AppendLine("        {").AppendLine("            global::System.ObjectDisposedException.ThrowIf(m_state.isPublicUseClosed, this);")
            .Append("            return (").Append(proxy.interfacePointerType).AppendLine(")m_state.nativeInterface;")
            .AppendLine("        }").AppendLine("    }")
            .AppendLine("    /// <summary>Creates a managed proxy with a caller-asserted native lifetime. stopAndJoinNative must prevent and join every native use of the interface.</summary>")
            .AppendLine("    /// <remarks>A managed callback failure is captured and reported as a C++ exception by this proxy. Native callers outside BGCS export boundaries must catch it or use an explicit native fallback adapter.</remarks>")
            .Append("    public ").Append(proxy.registrationName).Append(" Create").Append(proxy.name).Append("Callbacks(").Append(proxy.interfaceName)
            .AppendLine(" target, global::System.Action<nint> stopAndJoinNative)")
            .AppendLine("    {").AppendLine("        global::System.ArgumentNullException.ThrowIfNull(target);")
            .AppendLine("        global::System.ArgumentNullException.ThrowIfNull(stopAndJoinNative);")
            .AppendLine("        using var call = m_session.AcquireCall();")
            .Append("        ").Append(runtime).AppendLine(".NativeModuleLease? module = m_session.AcquireResource();")
            .Append("        var state = new ").Append(state).AppendLine("(this, target, stopAndJoinNative);")
            .AppendLine("        try").AppendLine("        {")
            .Append("            state.tokenRegistration = ").Append(runtime).Append(".CallbackRegistry<").Append(state).AppendLine(">.Register(state);")
            .Append("            state.registration = new ").Append(runtime).Append(".CallbackRegistration((nint)").Append(ThunkPointer(proxy, 0))
            .Append(", state.StopAndJoin, state, module, ").Append(runtime).AppendLine(".CallbackShutdownOrder.KeepOpenUntilUnregistered);")
            .AppendLine("            module = null;");
        CSharpMethodPlan create = plan.methods[proxy.create.methodIndex];
        Dictionary<int, string> factoryArguments = new() { [proxy.userDataIndex] = "(" + create.parameterTypes[proxy.userDataIndex] + ")state.tokenRegistration.token" };
        for (int index = 0; index < proxy.slots.Length; index++)
            factoryArguments.Add(proxy.slots[index].factoryParameterIndex, "(" + create.parameterTypes[proxy.slots[index].factoryParameterIndex] + ")" + ThunkPointer(proxy, index));
        text.Append("            state.proxy = (nint)").Append(createHelper).Append('(')
            .Append(string.Join(", ", Inputs(create, proxy.create).Select(index => factoryArguments[index]))).AppendLine(");")
            .AppendLine("            if (state.proxy == 0) throw new global::System.InvalidOperationException(\"Native virtual construction returned a null proxy.\");")
            .Append("            state.nativeInterface = (nint)").Append(castHelper).Append("((").Append(plan.methods[proxy.cast.methodIndex].parameterTypes[proxy.cast.instanceIndex]).AppendLine(")state.proxy);")
            .AppendLine("            if (state.nativeInterface == 0) throw new global::System.InvalidOperationException(\"Native virtual adjustment returned a null interface.\");")
            .AppendLine("            state.published = true;").Append("            return new ").Append(proxy.registrationName).AppendLine("(state);")
            .AppendLine("        }").AppendLine("        catch (global::System.Exception constructionFailure)").AppendLine("        {")
            .AppendLine("            try").AppendLine("            {")
            .AppendLine("                if (state.registration is not null) state.registration.Dispose();")
            .AppendLine("                else { state.tokenRegistration?.Dispose(); module?.Dispose(); }")
            .AppendLine("            }").AppendLine("            catch (global::System.Exception cleanupFailure)")
            .AppendLine("            { throw new global::System.AggregateException(\"Native virtual construction and cleanup failed; retained state remains rooted.\", constructionFailure, cleanupFailure); }")
            .AppendLine("            throw;").AppendLine("        }").AppendLine("    }")
            .AppendLine("    /// <summary>Runs one caller-asserted nonretaining native operation. The operation must return only after all native uses of this interface have ended.</summary>")
            .Append("    public TResult With").Append(proxy.name).Append("Callbacks<TResult>(").Append(proxy.interfaceName).Append(" target, ").Append(nativeCall).AppendLine("<TResult> operation)")
            .AppendLine("    {").AppendLine("        global::System.ArgumentNullException.ThrowIfNull(operation);")
            .Append("        using var registration = Create").Append(proxy.name).AppendLine("Callbacks(target, static _ => { });")
            .AppendLine("        return registration.Invoke(operation);").AppendLine("    }");
        EmitOperation(text, plan, proxy.create, createHelper, runtime);
        EmitOperation(text, plan, proxy.destroy, destroyHelper, runtime);
        EmitOperation(text, plan, proxy.cast, castHelper, runtime);
        text.Append("    internal sealed class ").Append(state).AppendLine(" : global::System.IDisposable")
            .AppendLine("    {").AppendLine("        private readonly object m_gate = new();")
            .Append("        private readonly ").Append(context).AppendLine(" m_context;")
            .AppendLine("        private readonly global::System.Action<nint> m_stopAndJoin;")
            .AppendLine("        private bool m_stopping;").AppendLine("        private int m_nativeUses;")
            .Append("        internal readonly ").Append(proxy.interfaceName).AppendLine(" target;")
            .Append("        internal readonly ").Append(runtime).AppendLine(".CallbackExceptionBoundary exceptions = new();")
            .Append("        internal ").Append(runtime).Append(".CallbackRegistry<").Append(state).AppendLine(">.Registration? tokenRegistration;")
            .Append("        internal ").Append(runtime).AppendLine(".CallbackRegistration? registration;")
            .AppendLine("        internal nint proxy;").AppendLine("        internal nint nativeInterface;").AppendLine("        internal bool published;")
            .AppendLine("        internal bool isPublicUseClosed => m_context.m_session.isClosing || registration!.isClosing;")
            .Append("        internal ").Append(state).Append('(').Append(context).Append(" context, ").Append(proxy.interfaceName).AppendLine(" target, global::System.Action<nint> stopAndJoin)")
            .AppendLine("        { m_context = context; this.target = target; m_stopAndJoin = stopAndJoin; }")
            .AppendLine("        internal NativeUse EnterNativeUse()")
            .AppendLine("        {").AppendLine("            lock (m_gate)").AppendLine("            {")
            .AppendLine("                global::System.ObjectDisposedException.ThrowIf(m_stopping || isPublicUseClosed, this);")
            .AppendLine("                if (!registration!.TryEnterInvocation(out var invocation)) throw new global::System.ObjectDisposedException(GetType().Name);")
            .AppendLine("                m_nativeUses++;").AppendLine("                return new NativeUse(this, invocation!);")
            .AppendLine("            }").AppendLine("        }")
            .AppendLine("        internal void StopAndJoin()")
            .AppendLine("        {").AppendLine("            lock (m_gate)").AppendLine("            {")
            .AppendLine("                m_stopping = true;")
            .AppendLine("                while (m_nativeUses != 0) global::System.Threading.Monitor.Wait(m_gate);")
            .AppendLine("            }").AppendLine("            if (published) m_stopAndJoin(nativeInterface);").AppendLine("        }")
            .AppendLine("        public void Dispose()")
            .AppendLine("        {").AppendLine("            if (proxy != 0)").AppendLine("            {")
            .Append("                m_context.").Append(destroyHelper).Append("((").Append(plan.methods[proxy.destroy.methodIndex].parameterTypes[proxy.destroy.instanceIndex]).AppendLine(")proxy);")
            .AppendLine("                proxy = 0;").AppendLine("            }").AppendLine("            tokenRegistration?.Dispose();").AppendLine("        }")
            .AppendLine("        internal sealed class NativeUse : global::System.IDisposable")
            .AppendLine("        {").Append("            private ").Append(state).AppendLine("? m_owner;")
            .Append("            private readonly ").Append(runtime).AppendLine(".CallbackRegistration.InvocationLease m_invocation;")
            .Append("            internal NativeUse(").Append(state).Append(" owner, ").Append(runtime).AppendLine(".CallbackRegistration.InvocationLease invocation)")
            .AppendLine("            { m_owner = owner; m_invocation = invocation; }")
            .AppendLine("            public void Dispose()")
            .AppendLine("            {").AppendLine("                var owner = global::System.Threading.Interlocked.Exchange(ref m_owner, null);")
            .AppendLine("                if (owner is null) return;")
            .AppendLine("                lock (owner.m_gate) { owner.m_nativeUses--; global::System.Threading.Monitor.PulseAll(owner.m_gate); }")
            .AppendLine("                m_invocation.Dispose();").AppendLine("            }").AppendLine("        }").AppendLine("    }");
        for (int index = 0; index < proxy.slots.Length; index++) EmitThunk(text, proxy, index, state, runtime);
        return text.AppendLine("}").ToString();
    }

    private static void EmitOperation(
        StringBuilder text,
        CSharpEmissionPlan plan,
        CSharpVirtualOperationPlan operation,
        string name,
        string runtime
    ) {
        CSharpMethodPlan method = plan.methods[operation.methodIndex];
        string[] arguments = Enumerable.Range(0, method.parameterTypes.Length).Select(index => "argument" + index).ToArray();
        arguments[operation.errorIndex] = "&error";
        if (operation.resultIndex >= 0) arguments[operation.resultIndex] = "&result";
        text.Append("    private ").Append(operation.resultType).Append(' ').Append(name).Append('(')
            .Append(string.Join(", ", Inputs(method, operation).Select(index => method.parameterTypes[index] + " argument" + index))).AppendLine(")")
            .AppendLine("    {").Append("        ").Append(operation.errorType).AppendLine(" error = default;");
        if (operation.resultIndex >= 0) text.Append("        ").Append(operation.resultType).AppendLine(" result = default;");
        text.AppendLine("        try").AppendLine("        {").Append("            int status = ").Append(Invoke(plan, operation.methodIndex))
            .Append('(').Append(string.Join(", ", arguments)).AppendLine(");")
            .Append("            if (status != ").Append(operation.successValue.ToString(CultureInfo.InvariantCulture)).Append(") throw new ").Append(runtime)
            .Append(".NativeCallException(status, ").Append(ContextEmitter.Literal(method.exportName)).AppendLine(");");
        if (operation.resultIndex >= 0) text.AppendLine("            return result;");
        text.AppendLine("        }").AppendLine("        finally").AppendLine("        {")
            .Append("            if (error != null) ").Append(Invoke(plan, operation.errorReleaseIndex)).AppendLine("(error);")
            .AppendLine("        }").AppendLine("    }");
    }

    private static void EmitThunk(
        StringBuilder text,
        CSharpVirtualProxyPlan proxy,
        int index,
        string state,
        string runtime
    ) {
        CSharpVirtualSlotPlan slot = proxy.slots[index];
        text.AppendLine("    [global::System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new global::System.Type[] { typeof(global::System.Runtime.CompilerServices.CallConvCdecl) })]")
            .Append("    private static int ").Append(Thunk(proxy, index)).Append('(')
            .Append(string.Join(", ", slot.nativeParameterTypes.Select((
                type,
                parameter
            ) => type + " argument" + parameter))).AppendLine(")")
            .AppendLine("    {").AppendLine("        int status = 1;").Append("        ").Append(state).AppendLine("? state = null;")
            .Append("        ").Append(runtime).AppendLine(".CallbackRegistration.InvocationLease? invocation = null;")
            .AppendLine("        try").AppendLine("        {")
            .Append("            if (!").Append(runtime).Append(".CallbackRegistry<").Append(state).AppendLine(">.TryGet((nint)argument0, out state)")
            .AppendLine("                || state.registration is null || !state.registration.TryEnterInvocation(out invocation)) return 1;");
        if (slot.result is { } output)
        {
            text.Append("            if (argument").Append(output.contract.index).AppendLine(" == null) return 1;")
                .Append("            *argument").Append(output.contract.index).AppendLine(" = default;");
        }
        string call = "state.target." + slot.name + "(" + string.Join(", ", slot.parameters.Select(parameter =>
            Input(parameter, "argument" + parameter.contract.index.ToString(CultureInfo.InvariantCulture)))) + ")";
        if (slot.result is { } result)
            text.Append("            var value = ").Append(call).AppendLine(";")
                .Append("            *argument").Append(result.contract.index).Append(" = ").Append(Output(result, "value")).AppendLine(";");
        else text.Append("            ").Append(call).AppendLine(";");
        text.AppendLine("            status = 0;").AppendLine("        }")
            .AppendLine("        catch (global::System.Exception failure)")
            .AppendLine("        { try { state?.exceptions.Capture(failure); } catch { } }")
            .AppendLine("        finally").AppendLine("        {")
            .AppendLine("            try { invocation?.Dispose(); }")
            .AppendLine("            catch (global::System.Exception failure) { status = 1; try { state?.exceptions.Capture(failure); } catch { } }")
            .AppendLine("        }").AppendLine("        return status;").AppendLine("    }");
    }

    private static string Input(
        CSharpVirtualValuePlan plan,
        string value
    ) => plan.isPointer ? "(nint)" + value : plan.isBoolean ? value + " != 0" : value;
    private static string Output(
        CSharpVirtualValuePlan plan,
        string value
    ) => plan.isPointer ? "(" + plan.nativeType + ")" + value : plan.isBoolean ? "(byte)(" + value + " ? 1 : 0)" : value;
    private static IEnumerable<int> Inputs(
        CSharpMethodPlan method,
        CSharpVirtualOperationPlan operation
    )
        => Enumerable.Range(0, method.parameterTypes.Length).Where(index => index != operation.errorIndex && index != operation.resultIndex);
    private static string Invoke(
        CSharpEmissionPlan plan,
        int index
    ) => plan.importMode == BindingImportMode.FunctionTable ? "m_function" + index
        : "global::" + CSharpNames.Namespace(plan.api.@namespace) + "." + plan.contextName + ".NativeImports.Function" + index;
    internal static string Helper(
        CSharpVirtualProxyPlan plan,
        string role
    ) => "__Bgcs" + plan.name + "Callback" + role;
    internal static string Thunk(
        CSharpVirtualProxyPlan plan,
        int index
    ) => "__Bgcs" + plan.name + "CallbackThunk" + index.ToString(CultureInfo.InvariantCulture);
    private static string ThunkPointer(
        CSharpVirtualProxyPlan plan,
        int index
    ) => "(delegate* unmanaged[Cdecl]<"
        + string.Join(", ", plan.slots[index].nativeParameterTypes.Append("int")) + ">)&" + Thunk(plan, index);
}
