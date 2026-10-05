using BGCS.Intermediate;

namespace BGCS.Configuration;

/// <summary>
/// Overrides inferred marshalling semantics for one native value.
/// </summary>
public sealed class MarshallingMapping
{
    /// <summary>Gets or sets the marshalling strategy override.</summary>
    public MarshallingStrategy? strategy { get; set; }
    /// <summary>Gets or sets the native ownership override.</summary>
    public BindingOwnership? ownership { get; set; }
    /// <summary>Gets or sets the native string encoding override.</summary>
    public BindingStringEncoding? encoding { get; set; }
    /// <summary>Gets or sets the related native length parameter name.</summary>
    public string? lengthParameter { get; set; }
    /// <summary>Gets or sets the related native capacity parameter name.</summary>
    public string? capacityParameter { get; set; }
    /// <summary>Gets or sets the related native written-count parameter name.</summary>
    public string? writtenCountParameter { get; set; }
    /// <summary>Gets or sets the native cleanup function used for owned values.</summary>
    public string? cleanupFunction { get; set; }
    /// <summary>Gets or sets whether generated marshalling must perform cleanup.</summary>
    public bool? requiresCleanup { get; set; }
    /// <summary>Gets or sets whether a string or sequence uses a terminating zero element.</summary>
    public bool? nullTerminated { get; set; }
    /// <summary>Gets or sets the allocator domain responsible for owned native storage.</summary>
    public BindingAllocatorKind? allocatorKind { get; set; }
    /// <summary>Gets or sets the native allocation function paired with cleanup.</summary>
    public string? allocatorFunction { get; set; }
    /// <summary>Gets or sets how long native code may retain a callback.</summary>
    public BindingCallbackLifetime? callbackLifetime { get; set; }
    /// <summary>Gets or sets the native callback threading contract.</summary>
    public BindingCallbackThreading? callbackThreading { get; set; }
    /// <summary>Gets or sets the function that synchronously unregisters a retained callback.</summary>
    public string? unregisterFunction { get; set; }
    /// <summary>Gets or sets the native asynchronous completion mechanism.</summary>
    public BindingAsyncCompletion? asyncCompletion { get; set; }
    /// <summary>Gets or sets the completion, polling, wait, or cancellation function.</summary>
    public string? completionFunction { get; set; }
}
