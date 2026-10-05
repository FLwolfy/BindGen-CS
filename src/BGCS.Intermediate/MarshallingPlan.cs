namespace BGCS.Intermediate;

/// <summary>
/// Identifies the generated marshalling strategy for a parameter or return value.
/// </summary>
public enum MarshallingStrategy
{
    /// <summary>
    /// The managed carrier has a directly compatible unmanaged layout.
    /// </summary>
    Blittable,
    /// <summary>
    /// The call passes an unmanaged address directly.
    /// </summary>
    Pointer,
    /// <summary>
    /// The call passes a wrapper around a native identity.
    /// </summary>
    Handle,
    /// <summary>
    /// The call converts encoded native text.
    /// </summary>
    String,
    /// <summary>
    /// The call converts a bounded managed buffer view.
    /// </summary>
    Span,
    /// <summary>
    /// The call passes a managed callback with an explicit lifetime.
    /// </summary>
    Callback,
    /// <summary>
    /// The call represents an optional native value.
    /// </summary>
    Optional,
    /// <summary>
    /// A registered conversion supplies the carrier and lifecycle.
    /// </summary>
    Custom
}

/// <summary>
/// Identifies native memory ownership at an interop boundary.
/// </summary>
public enum BindingOwnership
{
    /// <summary>
    /// Ownership has not been proven.
    /// </summary>
    Unknown,
    /// <summary>
    /// The native owner retains storage; the receiver must not release it.
    /// </summary>
    Borrowed,
    /// <summary>
    /// Storage participates in an explicitly declared shared lifetime.
    /// </summary>
    Shared,
    /// <summary>
    /// The call transfers responsibility to the receiving side.
    /// </summary>
    Transferred,
    /// <summary>
    /// The receiver owns storage and must use the declared cleanup operation.
    /// </summary>
    Owned,
    /// <summary>
    /// The caller supplies storage and retains its allocation lifetime.
    /// </summary>
    CallerAllocated
}

/// <summary>
/// Identifies the character encoding used by a native string.
/// </summary>
public enum BindingStringEncoding
{
    /// <summary>
    /// The value is not treated as encoded text.
    /// </summary>
    None,
    /// <summary>
    /// The native API uses its declared narrow character encoding.
    /// </summary>
    Ansi,
    /// <summary>
    /// The native API uses UTF-8 bytes.
    /// </summary>
    Utf8,
    /// <summary>
    /// The native API uses UTF-16 code units.
    /// </summary>
    Utf16,
    /// <summary>
    /// The native API uses UTF-32 code units.
    /// </summary>
    Utf32
}

/// <summary>Identifies the allocator domain responsible for native storage.</summary>
public enum BindingAllocatorKind
{
    /// <summary>
    /// The allocation domain is unknown.
    /// </summary>
    Unspecified,
    /// <summary>
    /// The caller supplies and releases storage.
    /// </summary>
    Caller,
    /// <summary>
    /// A declared native function allocates storage.
    /// </summary>
    NativeFunction,
    /// <summary>
    /// Storage uses the COM task allocator.
    /// </summary>
    CoTaskMem,
    /// <summary>
    /// Storage uses the declared C runtime allocator.
    /// </summary>
    CRuntime,
    /// <summary>
    /// A consumer-declared allocator contract owns storage.
    /// </summary>
    Custom
}

/// <summary>Defines how long native code may retain a callback pointer.</summary>
public enum BindingCallbackLifetime
{
    /// <summary>
    /// Native callback retention is unknown.
    /// </summary>
    Unspecified,
    /// <summary>
    /// Native code may invoke the callback only before the initiating call returns.
    /// </summary>
    CallOnly,
    /// <summary>
    /// Native code retains the callback until synchronous unregister completes.
    /// </summary>
    RetainedUntilUnregister,
    /// <summary>
    /// Native code retains the callback until terminal operation completion.
    /// </summary>
    RetainedUntilCompletion,
    /// <summary>
    /// The consumer explicitly manages native callback retention.
    /// </summary>
    Manual
}

/// <summary>Defines the threads on which native code may invoke a callback.</summary>
public enum BindingCallbackThreading
{
    /// <summary>
    /// Permitted callback threads are unknown.
    /// </summary>
    Unspecified,
    /// <summary>
    /// Callbacks run on the initiating thread.
    /// </summary>
    CallerThread,
    /// <summary>
    /// Callbacks may run concurrently on native threads.
    /// </summary>
    AnyThread,
    /// <summary>
    /// Native code serializes callback invocation on its declared thread.
    /// </summary>
    SerializedNativeThread
}

/// <summary>Defines how an asynchronous native operation announces terminal completion.</summary>
public enum BindingAsyncCompletion
{
    /// <summary>
    /// The native operation completes synchronously.
    /// </summary>
    None,
    /// <summary>
    /// A terminal callback announces completion.
    /// </summary>
    Callback,
    /// <summary>
    /// The consumer polls a declared completion query.
    /// </summary>
    Polling,
    /// <summary>
    /// The consumer waits on a declared completion handle.
    /// </summary>
    WaitHandle,
    /// <summary>
    /// A consumer-declared mechanism announces terminal completion.
    /// </summary>
    Custom
}

/// <summary>
/// Defines a complete native-to-managed conversion plan consumed by emitters.
/// </summary>
/// <param name = "strategy">Conversion strategy.</param>
/// <param name = "ownership">Memory ownership contract.</param>
/// <param name = "stringEncoding">String encoding when applicable.</param>
/// <param name = "lengthParameter">Related element-count parameter, when known.</param>
/// <param name = "capacityParameter">Related capacity parameter, when known.</param>
/// <param name = "writtenCountParameter">Related output-count parameter, when known.</param>
/// <param name = "requiresCleanup">Whether generated code must execute cleanup after the native call.</param>
/// <param name = "cleanupFunction">Native cleanup function for an owned value, when configured.</param>
/// <param name = "nullTerminated">Whether a string or sequence uses a terminating zero element.</param>
/// <param name = "allocatorKind">Allocator domain used by owned native storage.</param>
/// <param name = "allocatorFunction">Native allocation function when the allocator is explicitly paired.</param>
/// <param name = "callbackLifetime">Native retention contract for callback pointers.</param>
/// <param name = "callbackThreading">Threading contract for callback invocation.</param>
/// <param name = "unregisterFunction">Native function that synchronously stops retained callback invocation.</param>
/// <param name = "asyncCompletion">Completion mechanism for asynchronous native work.</param>
/// <param name = "completionFunction">Native completion, wait, poll, or cancellation function required by the contract.</param>
public sealed record MarshallingPlan(
    MarshallingStrategy strategy,
    BindingOwnership ownership,
    BindingStringEncoding stringEncoding = BindingStringEncoding.None,
    string? lengthParameter = null,
    string? capacityParameter = null,
    string? writtenCountParameter = null,
    bool requiresCleanup = false,
    string? cleanupFunction = null,
    bool nullTerminated = false,
    BindingAllocatorKind allocatorKind = BindingAllocatorKind.Unspecified,
    string? allocatorFunction = null,
    BindingCallbackLifetime callbackLifetime = BindingCallbackLifetime.Unspecified,
    BindingCallbackThreading callbackThreading = BindingCallbackThreading.Unspecified,
    string? unregisterFunction = null,
    BindingAsyncCompletion asyncCompletion = BindingAsyncCompletion.None,
    string? completionFunction = null
);
