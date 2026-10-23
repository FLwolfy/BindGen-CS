using BGCS.Core.Interop;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

internal static class EventLoopCallbackStateEmitter
{
    internal static string Emit(
        CSharpEmissionPlan plan,
        CSharpRetainedCallbackPlan operation,
        string runtime,
        string stop
    ) {
        CSharpCallbackInputPlan callback = operation.callback;
        string state = callback.stateName;
        string stopArguments = "(" + operation.handleType + ")nativeHandle"
            + (operation.kind == NativeCallbackRegistrationKind.CallbackAndUserData ? ", callbackPointer" : string.Empty);
        return $$"""
            internal sealed class {{state}} : global::System.IDisposable
            {
                private readonly object m_gate = new();
                private readonly {{plan.contextName}} m_context;
                private bool m_ready, m_stopping;
                private int m_nativeUses;
                private global::System.Exception? m_creationFailure;
                private readonly global::System.Threading.Tasks.TaskCompletionSource m_published = new(global::System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
                private global::System.Threading.Tasks.TaskCompletionSource m_drained = CompletedDrain();
                internal readonly {{runtime}}.NativeCallbackDispatcher dispatcher;
                internal readonly nint callbackPointer;
                internal readonly {{callback.delegateName}} target;
                internal readonly {{runtime}}.CallbackExceptionBoundary exceptions = new();
                internal {{runtime}}.CallbackRegistry<{{state}}>.Registration? token;
                internal {{runtime}}.CallbackRegistration? registration;
                internal nint nativeHandle;
                internal bool isPublicUseClosed => m_context.m_session.isClosing || registration!.isClosing;
                internal {{state}}({{plan.contextName}} context, {{callback.delegateName}} target)
                { m_context = context; dispatcher = context.m_callbackDispatcher; this.target = target; callbackPointer = (nint){{RetainedCallbackEmitter.ThunkPointer(callback)}}; }
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
                internal NativeUse EnterNativeUse()
                {
                    dispatcher.VerifyAccess();
                    lock (m_gate)
                    {
                        global::System.ObjectDisposedException.ThrowIf(m_stopping || isPublicUseClosed, this);
                        if (!registration!.TryEnterInvocation(out var invocation)) throw new global::System.ObjectDisposedException(GetType().Name);
                        if (m_nativeUses == 0) m_drained = new(global::System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
                        m_nativeUses++; return new NativeUse(this, invocation!);
                    }
                }
                internal global::System.Threading.Tasks.ValueTask StopAndJoinAsync()
                {
                    global::System.Threading.Tasks.Task ready, drained;
                    lock (m_gate) { m_stopping = true; ready = m_published.Task; drained = m_drained.Task; }
                    return new(dispatcher.InvokeAfterAsync(global::System.Threading.Tasks.Task.WhenAll(ready, drained), StopReady));
                }
                private void StopReady()
                {
                    dispatcher.VerifyAccess();
                    lock (m_gate)
                    {
                        if (!m_ready || m_nativeUses != 0) throw new global::System.InvalidOperationException("The event-loop native registration has not drained.");
                        if (m_creationFailure is not null) throw new global::System.InvalidOperationException("The native registration call did not establish a safely releasable handle.", m_creationFailure);
                    }
                    if (nativeHandle != 0) m_context.{{stop}}({{stopArguments}});
                }
                public void Dispose() => token?.Dispose();
                internal sealed class NativeUse : global::System.IDisposable
                {
                    private {{state}}? m_owner;
                    private readonly {{runtime}}.CallbackRegistration.InvocationLease m_invocation;
                    internal NativeUse({{state}} owner, {{runtime}}.CallbackRegistration.InvocationLease invocation)
                    { m_owner = owner; m_invocation = invocation; }
                    public void Dispose()
                    {
                        var owner = global::System.Threading.Interlocked.Exchange(ref m_owner, null); if (owner is null) return;
                        lock (owner.m_gate) { if (--owner.m_nativeUses == 0) owner.m_drained.TrySetResult(); }
                        m_invocation.Dispose();
                    }
                }
            }
            """;
    }
}
