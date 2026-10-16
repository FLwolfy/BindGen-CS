using BGCS.Contracts.Interop;

namespace BGCS.Configuration;

/// <summary>Declares one callback's native lifetime, threading and exception fallback without inferring retention.</summary>
public sealed record CallbackParameterConfiguration
{
    /// <summary>Gets the exact native export or unambiguous original callable.</summary>
    public string function { get; init; } = string.Empty;
    /// <summary>Gets the original callback parameter name.</summary>
    public string parameter { get; init; } = string.Empty;
    /// <summary>Gets the explicitly declared native validity boundary.</summary>
    public NativeLifetimeKind lifetime { get; init; }
    /// <summary>Gets whether native code retains the callback, or null when unknown.</summary>
    public bool? retained { get; init; }
    /// <summary>Gets the explicitly declared callback execution threads.</summary>
    public NativeCallbackThreading threading { get; init; }
    /// <summary>Gets an outer void-pointer parameter receiving a generated non-reused state token.</summary>
    public string? userDataParameter { get; init; }
    /// <summary>Gets the zero-based callback argument carrying that same opaque state token.</summary>
    public int? callableUserDataIndex { get; init; }
    /// <summary>Gets invariant scalar fallback data, or the literal null string for a pointer result; void callbacks omit this field.</summary>
    public string? failureReturn { get; init; }
    /// <summary>Gets an exact native unregister operation for a retained protocol.</summary>
    public string? unregisterFunction { get; init; }
    /// <summary>Gets the independently asserted guarantee when native unregister returns.</summary>
    public NativeQuiescenceKind unregisterQuiescence { get; init; }
    /// <summary>Gets the explicitly declared registration identity and its stop-and-join protocol.</summary>
    public CallbackRegistrationConfiguration? registration { get; init; }
}

/// <summary>Declares the exact returned-handle or callback/user-data identity consumed by native stop-and-join.</summary>
public sealed record CallbackRegistrationConfiguration
{
    /// <summary>Gets whether native unregister uses a returned pointer or the same callback/user-data pair.</summary>
    public NativeCallbackRegistrationKind kind { get; init; }
    /// <summary>Gets the exact native unregister parameter receiving the returned handle.</summary>
    public string unregisterHandleParameter { get; init; } = string.Empty;
    /// <summary>Gets the guarantee when native registration returns null; no active native use is required before failed-registration resources can be released.</summary>
    public NativeQuiescenceKind nullResultQuiescence { get; init; }
    /// <summary>Gets the explicitly asserted native shutdown thread domain; unknown does not permit an owning managed registration.</summary>
    public string releaseThreadDomain { get; init; } = "unknown";
    /// <summary>Gets the native unregister input receiving the identical callback pointer for a callback/state protocol.</summary>
    public string? unregisterCallbackParameter { get; init; }
    /// <summary>Gets the native unregister input receiving the identical user-data token for a callback/state protocol.</summary>
    public string? unregisterUserDataParameter { get; init; }
    /// <summary>Gets invariant scalar data identifying successful callback/state registration; void factories omit it.</summary>
    public string? successfulReturn { get; init; }
    /// <summary>Gets the guarantee when callback/state registration returns a result other than success.</summary>
    public NativeQuiescenceKind failureResultQuiescence { get; init; }
    /// <summary>Gets an explicit assertion that native unregister establishes full quiescence without blocking the captured event-loop thread.</summary>
    public bool? unregisterIsNonBlocking { get; init; }
}
