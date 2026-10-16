using System.Collections.Immutable;
using BGCS.Contracts.Interop;

namespace BGCS.CSharp.Planning;

/// <summary>Freezes one synchronous status/error boundary used inside an already retained call.</summary>
/// <param name="methodIndex">The exact unchanged native import.</param>
/// <param name="inputIndices">The complete ordered inputs excluding result and error channels.</param>
/// <param name="resultIndex">The result output index, or minus one.</param>
/// <param name="resultType">The exact C# output pointee type, or void.</param>
/// <param name="errorIndex">The exact owned error output.</param>
/// <param name="errorReleaseIndex">The matching error cleanup import.</param>
public sealed record CSharpScopedStatusCallPlan(
    int methodIndex,
    ImmutableArray<int> inputIndices,
    int resultIndex,
    string resultType,
    int errorIndex,
    int errorReleaseIndex
);

/// <summary>Copies a fresh native text value and destroys it before its creating managed call returns.</summary>
/// <param name="factory">The native factory and owned result channel.</param>
/// <param name="count">The exact native code-unit count operation.</param>
/// <param name="copy">The single bounded native copy operation and written-count result.</param>
/// <param name="release">The matching same-creating-call destruction operation.</param>
/// <param name="signature">The checked managed text-returning facade.</param>
/// <param name="encoding">The declared Unicode interpretation.</param>
/// <param name="codeUnitWidth">The exact native code-unit byte width.</param>
/// <param name="byteOrder">The native code-unit byte order.</param>
/// <param name="maximumCodeUnits">The positive copy bound excluding termination.</param>
/// <param name="copyAlignment">The actual native copy destination alignment.</param>
public sealed record CSharpValueStringResultPlan(
    CSharpScopedStatusCallPlan factory,
    CSharpScopedStatusCallPlan count,
    CSharpScopedStatusCallPlan copy,
    CSharpScopedStatusCallPlan release,
    CSharpFacadeMethodPlan signature,
    NativeTextEncoding encoding,
    int codeUnitWidth,
    NativeTextByteOrder byteOrder,
    int maximumCodeUnits,
    int copyAlignment
)
{
    /// <summary>Gets an existing owned object's checked text member, without transferring its receiver ownership.</summary>
    public CSharpValueStringReceiverPlan? receiver { get; init; }

    /// <summary>Gets whether an unknown-retention native self must be supplied exclusively through a proved owned wrapper.</summary>
    public bool requiresOwnedReceiver { get; init; }
}

/// <summary>Forwards an owned instance text result through the same native value copy and cleanup plan.</summary>
/// <param name="objectName">The existing nested owned wrapper.</param>
/// <param name="receiverIndex">The exact native receiver input.</param>
/// <param name="signature">The public object member with only the receiver and checked count channels hidden.</param>
public sealed record CSharpValueStringReceiverPlan(
    string objectName,
    int receiverIndex,
    CSharpFacadeMethodPlan signature
);
