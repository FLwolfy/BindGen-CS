using System.Collections.Immutable;
using BGCS.Core.Interop;

namespace BGCS.Configuration;

/// <summary>Declares one returned-handle asynchronous operation whose completion is drained through its callback registration's native join.</summary>
public sealed record AsyncOperationConfiguration
{
    /// <summary>Gets the exact native start export.</summary>
    public string function { get; init; } = string.Empty;
    /// <summary>Gets the explicitly retained completion callback parameter.</summary>
    public string completionCallback { get; init; } = string.Empty;
    /// <summary>Gets the guarantee at the terminal callback; full native-frame lifetime still ends only at the separate stop-and-join operation.</summary>
    public NativeQuiescenceKind completionQuiescence { get; init; }
    /// <summary>Gets the zero-based completion argument containing the scalar result.</summary>
    public int resultArgumentIndex { get; init; }
    /// <summary>Gets the zero-based completion argument containing the terminal native status.</summary>
    public int statusArgumentIndex { get; init; }
    /// <summary>Gets invariant integral data for native success.</summary>
    public string successStatus { get; init; } = "0";
    /// <summary>Gets invariant integral data for acknowledged native cancellation, or null for no cancellation status.</summary>
    public string? cancellationStatus { get; init; }
    /// <summary>Gets an exact native cancellation-request export; requesting cancellation does not release native resources.</summary>
    public string? cancelFunction { get; init; }
    /// <summary>Gets the cancellation input receiving the returned native handle.</summary>
    public string? cancelHandleParameter { get; init; }
    /// <summary>Gets the native guarantee after the cancellation request returns.</summary>
    public NativeQuiescenceKind cancellationQuiescence { get; init; }
    /// <summary>Gets an explicit assertion that the native cancellation request does not block the captured event-loop thread.</summary>
    public bool? cancellationIsNonBlocking { get; init; }
    /// <summary>Gets explicitly retained buffer parameters whose storage remains pinned until native stop-and-join.</summary>
    public ImmutableArray<string> retainedParameters { get; init; } = [];
}
