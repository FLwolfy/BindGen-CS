using BGCS.Contracts.Common;

namespace BGCS.Contracts.Interop;

/// <summary>Describes an explicit C ABI failure boundary without allowing native exceptions to escape.</summary>
/// <param name="successValue">The exact native integral success value.</param>
/// <param name="evidence">The source of the native status protocol.</param>
public sealed record ErrorContract(
    string successValue,
    Evidence evidence
)
{
    /// <summary>Gets the parameter containing the original return value.</summary>
    public StableId? valueParameter { get; init; }
    /// <summary>Gets the parameter containing native error data.</summary>
    public StableId? errorParameter { get; init; }
    /// <summary>Gets error-data ownership and its exact release export.</summary>
    public OwnershipContract? errorOwnership { get; init; }
    /// <summary>Gets the error-text encoding and length contract.</summary>
    public StringContract? errorText { get; init; }
}
