namespace BGCS.Intermediate;

/// <summary>
/// Identifies the generated marshalling strategy for a parameter or return value.
/// </summary>
public enum MarshallingStrategy
{
    Blittable,
    Pointer,
    Handle,
    String,
    Span,
    Callback,
    Optional,
    Custom
}

/// <summary>
/// Identifies native memory ownership at an interop boundary.
/// </summary>
public enum BindingOwnership
{
    Unknown,
    Borrowed,
    Shared,
    Transferred,
    Owned,
    CallerAllocated
}

/// <summary>
/// Identifies the character encoding used by a native string.
/// </summary>
public enum BindingStringEncoding
{
    None,
    Ansi,
    Utf8,
    Utf16,
    Utf32
}

/// <summary>Identifies the allocator domain responsible for native storage.</summary>
public enum BindingAllocatorKind
{
    Unspecified,
    Caller,
    NativeFunction,
    CoTaskMem,
    CRuntime,
    Custom
}

/// <summary>Defines how long native code may retain a callback pointer.</summary>
public enum BindingCallbackLifetime
{
    Unspecified,
    CallOnly,
    RetainedUntilUnregister,
    RetainedUntilCompletion,
    Manual
}

/// <summary>Defines the threads on which native code may invoke a callback.</summary>
public enum BindingCallbackThreading
{
    Unspecified,
    CallerThread,
    AnyThread,
    SerializedNativeThread
}

/// <summary>Defines how an asynchronous native operation announces terminal completion.</summary>
public enum BindingAsyncCompletion
{
    None,
    Callback,
    Polling,
    WaitHandle,
    Custom
}

/// <summary>
/// Defines a complete native-to-managed conversion plan consumed by emitters.
/// </summary>
/// <param name="Strategy">Conversion strategy.</param>
/// <param name="Ownership">Memory ownership contract.</param>
/// <param name="StringEncoding">String encoding when applicable.</param>
/// <param name="LengthParameter">Related element-count parameter, when known.</param>
/// <param name="CapacityParameter">Related capacity parameter, when known.</param>
/// <param name="WrittenCountParameter">Related output-count parameter, when known.</param>
/// <param name="RequiresCleanup">Whether generated code must execute cleanup after the native call.</param>
/// <param name="CleanupFunction">Native cleanup function for an owned value, when configured.</param>
/// <param name="NullTerminated">Whether a string or sequence uses a terminating zero element.</param>
/// <param name="AllocatorKind">Allocator domain used by owned native storage.</param>
/// <param name="AllocatorFunction">Native allocation function when the allocator is explicitly paired.</param>
/// <param name="CallbackLifetime">Native retention contract for callback pointers.</param>
/// <param name="CallbackThreading">Threading contract for callback invocation.</param>
/// <param name="UnregisterFunction">Native function that synchronously stops retained callback invocation.</param>
/// <param name="AsyncCompletion">Completion mechanism for asynchronous native work.</param>
/// <param name="CompletionFunction">Native completion, wait, poll, or cancellation function required by the contract.</param>
public sealed record MarshallingPlan(MarshallingStrategy Strategy, BindingOwnership Ownership,
    BindingStringEncoding StringEncoding = BindingStringEncoding.None, string? LengthParameter = null,
    string? CapacityParameter = null, string? WrittenCountParameter = null, bool RequiresCleanup = false,
    string? CleanupFunction = null, bool NullTerminated = false,
    BindingAllocatorKind AllocatorKind = BindingAllocatorKind.Unspecified, string? AllocatorFunction = null,
    BindingCallbackLifetime CallbackLifetime = BindingCallbackLifetime.Unspecified,
    BindingCallbackThreading CallbackThreading = BindingCallbackThreading.Unspecified,
    string? UnregisterFunction = null, BindingAsyncCompletion AsyncCompletion = BindingAsyncCompletion.None,
    string? CompletionFunction = null);
