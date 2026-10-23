using System.Collections.Immutable;
using BGCS.Core.Common;

namespace BGCS.Core.Bridge;

/// <summary>Owns a native callback context through an explicitly declared returned-handle stop-and-join protocol.</summary>
/// <param name="registrationFunction">The original registration declaration.</param>
/// <param name="unregisterFunction">The original operation establishing complete native quiescence.</param>
/// <param name="unregisterHandleParameter">The original unregister input receiving the native handle.</param>
/// <param name="nativeHandleType">The exact returned native handle type.</param>
/// <param name="carrierType">The opaque bridge context handle returned to managed code.</param>
/// <param name="callbackParameters">Callbacks sharing the owned context.</param>
public sealed record NativeCallbackContextPlan(
    StableId registrationFunction,
    StableId unregisterFunction,
    StableId unregisterHandleParameter,
    StableId nativeHandleType,
    StableId carrierType,
    ImmutableArray<StableId> callbackParameters
)
{
    /// <summary>Gets an optional cancellation operation that borrows the same context without releasing it.</summary>
    public StableId? cancelFunction { get; init; }
    /// <summary>Gets the exact cancellation input receiving the native handle.</summary>
    public StableId? cancellationHandleParameter { get; init; }
}
