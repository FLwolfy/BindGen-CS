using System.Collections.Immutable;
using BGCS.Contracts.Common;

namespace BGCS.Contracts.Interop;

/// <summary>Separates cancellation requests from terminal native resource-access acknowledgement.</summary>
/// <param name="completionCallback">The native completion parameter identity.</param>
/// <param name="completionQuiescence">The resource-access guarantee established by completion.</param>
/// <param name="evidence">The source of the asynchronous protocol.</param>
public sealed record AsyncContract(
    StableId completionCallback,
    NativeQuiescenceKind completionQuiescence,
    Evidence evidence
)
{
    /// <summary>Gets the optional native cancellation export.</summary>
    public StableId? cancelFunction { get; init; }
    /// <summary>Gets the guarantee established when the cancellation call returns.</summary>
    public NativeQuiescenceKind cancellationQuiescence { get; init; }
    /// <summary>Gets parameters whose storage must be retained through terminal acknowledgement.</summary>
    public ImmutableArray<StableId> retainedParameters { get; init; } = [];
    /// <summary>Gets the completion callback argument carrying a scalar operation result.</summary>
    public int? resultArgumentIndex { get; init; }
    /// <summary>Gets the completion callback argument carrying a terminal native status.</summary>
    public int? statusArgumentIndex { get; init; }
    /// <summary>Gets the exact native success status; other statuses produce a managed failure.</summary>
    public BGCS.Contracts.Native.NativeScalarConstant? successStatus { get; init; }
    /// <summary>Gets the exact native acknowledged cancellation status, or null when none is declared.</summary>
    public BGCS.Contracts.Native.NativeScalarConstant? cancellationStatus { get; init; }
    /// <summary>Gets the exact cancellation input receiving the returned native registration handle.</summary>
    public StableId? cancellationHandleParameter { get; init; }
    /// <summary>Gets whether the optional native cancellation request returns without blocking the event-loop thread; null means unknown.</summary>
    public bool? cancellationIsNonBlocking { get; init; }
}
