using System.Collections.Immutable;
using BGCS.Core.Common;
using BGCS.Core.Interop;

namespace BGCS.Core.Bridge;

/// <summary>Requests an additional pointer-and-length export while preserving the primary opaque native export.</summary>
/// <param name="function">One exact original source signature, qualified callable or primary export.</param>
/// <param name="parameters">Original parameter names to adapt together in one additional export.</param>
public sealed record SequenceInputAdapterRequest(
    string function,
    ImmutableArray<SequenceInputParameterRequest> parameters
);

/// <summary>Selects one sequence argument and explicitly describes any borrowed native argument retention.</summary>
/// <param name="parameter">The original native parameter name.</param>
public sealed record SequenceInputParameterRequest(string parameter)
{
    /// <summary>Gets native retention of the sequence or its element addresses; null means unknown.</summary>
    public bool? retainsArgument { get; init; }
    /// <summary>Gets the declared lifetime of a borrowed view or temporary reference.</summary>
    public NativeLifetimeKind lifetime { get; init; } = NativeLifetimeKind.Unknown;
}

/// <summary>Identifies whether the native sequence owns a copied value or borrows the pinned C elements.</summary>
public enum StandardSequenceMaterialization
{
    /// <summary>Creates a temporary native value by checked element conversion.</summary>
    CopyValue,
    /// <summary>Creates a synchronous view over identical native element storage.</summary>
    BorrowedView
}

/// <summary>Stores finite native sequence construction independently of source-expression templates.</summary>
/// <param name="family">The compiler-recognized standard sequence family.</param>
/// <param name="elementType">The exact original native element type.</param>
/// <param name="materialization">The copy or borrowing operation.</param>
/// <param name="evidence">The source of nonretention and argument-lifetime facts.</param>
public sealed record StandardSequenceArgument(
    StandardValueKind family,
    StableId elementType,
    StandardSequenceMaterialization materialization,
    Evidence evidence
)
{
    /// <summary>Gets a fixed array or span extent, or null for a dynamic sequence.</summary>
    public ulong? extent { get; init; }
}
