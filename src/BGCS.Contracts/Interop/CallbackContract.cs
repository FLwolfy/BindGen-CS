using BGCS.Contracts.Common;
using BGCS.Contracts.Native;

namespace BGCS.Contracts.Interop;

/// <summary>Describes how native callback execution ends before managed resources can be freed.</summary>
public enum NativeQuiescenceKind
{
    /// <summary>No stopping or draining guarantee is known.</summary>
    Unknown,
    /// <summary>No new invocations start, but existing native execution may continue.</summary>
    NoNewInvocations,
    /// <summary>Native execution has fully stopped using the registered callback and its state.</summary>
    NoActiveNativeInvocations
}

/// <summary>Describes the native threads that may enter an explicitly retained managed callback.</summary>
public enum NativeCallbackThreading
{
    /// <summary>No callback thread guarantee is available.</summary>
    Unknown,
    /// <summary>Callbacks execute synchronously on the thread making the native call.</summary>
    CallerThread,
    /// <summary>Native code may enter callbacks from arbitrary threads within the declared lifetime.</summary>
    AnyThread,
    /// <summary>Callbacks and native registration uses are serialized on the registration's captured event-loop dispatcher.</summary>
    EventLoop
}

/// <summary>Preserves a retained callback's registration and native quiescence protocol.</summary>
/// <param name="callableType">The exact native callback type.</param>
/// <param name="lifetime">The callback validity boundary.</param>
/// <param name="evidence">The source of the callback protocol.</param>
public sealed record CallbackContract(
    StableId callableType,
    LifetimeContract lifetime,
    Evidence evidence
)
{
    /// <summary>Gets pointer retention, or null when unknown.</summary>
    public bool? retained { get; init; }
    /// <summary>Gets the native unregister export.</summary>
    public StableId? unregisterFunction { get; init; }
    /// <summary>Gets the native user-data parameter identity.</summary>
    public StableId? userDataParameter { get; init; }
    /// <summary>Gets the guarantee established when unregister completes.</summary>
    public NativeQuiescenceKind unregisterQuiescence { get; init; }
    /// <summary>Gets native reentrancy support, or null when unknown.</summary>
    public bool? reentrant { get; init; }
    /// <summary>Gets the explicitly asserted native callback execution threads.</summary>
    public NativeCallbackThreading threading { get; init; }
    /// <summary>Gets the zero-based callback argument carrying the outer user-data parameter, or null for a delegate-backed protocol.</summary>
    public int? callableUserDataIndex { get; init; }
    /// <summary>Gets the exact native scalar returned when managed callback code fails.</summary>
    public NativeScalarConstant? failureReturn { get; init; }
    /// <summary>Gets the explicit null-pointer fallback for a pointer-returning callback, or null when undeclared.</summary>
    public bool? failureReturnsNull { get; init; }
    /// <summary>Gets an explicit returned-handle registration protocol, or null when the native registration shape is unknown.</summary>
    public CallbackRegistrationContract? registration { get; init; }
}
