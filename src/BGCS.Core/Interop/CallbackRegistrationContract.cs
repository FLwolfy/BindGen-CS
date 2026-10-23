using BGCS.Core.Common;
using BGCS.Core.Native;

namespace BGCS.Core.Interop;

/// <summary>Describes a native callback registration identity released by an exact stop-and-join operation.</summary>
/// <param name="unregisterHandleParameter">The exact native unregister input receiving a returned registration handle, or null for a callback/user-data identity.</param>
/// <param name="nullResultQuiescence">The native guarantee when registration returns a null handle.</param>
/// <param name="releaseThreadDomain">The explicitly declared threads allowed to execute native stop-and-join.</param>
/// <param name="evidence">The source of this handle and shutdown protocol.</param>
public sealed record CallbackRegistrationContract(
    StableId? unregisterHandleParameter,
    NativeQuiescenceKind nullResultQuiescence,
    string releaseThreadDomain,
    Evidence evidence
)
{
    /// <summary>Gets the exact native identity used to unregister this callback.</summary>
    public NativeCallbackRegistrationKind kind { get; init; }
    /// <summary>Gets the native unregister input receiving the identical generated callback pointer.</summary>
    public StableId? unregisterCallbackParameter { get; init; }
    /// <summary>Gets the native unregister input receiving the identical opaque user-data token.</summary>
    public StableId? unregisterUserDataParameter { get; init; }
    /// <summary>Gets the exact successful registration return for a callback/state pair; void registration omits it.</summary>
    public NativeScalarConstant? successfulReturn { get; init; }
    /// <summary>Gets the native guarantee after a callback/state registration reports failure.</summary>
    public NativeQuiescenceKind failureResultQuiescence { get; init; }
    /// <summary>Gets whether native unregister establishes its declared full quiescence without blocking the event-loop thread; null means unknown.</summary>
    public bool? unregisterIsNonBlocking { get; init; }
}

/// <summary>Identifies the explicitly declared native callback registration identity.</summary>
public enum NativeCallbackRegistrationKind
{
    /// <summary>A successful native factory returns a pointer consumed by stop-and-join.</summary>
    ReturnedHandle,
    /// <summary>Native unregister consumes exactly the registered callback pointer and opaque user data.</summary>
    CallbackAndUserData
}
