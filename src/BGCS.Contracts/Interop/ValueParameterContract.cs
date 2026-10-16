using BGCS.Contracts.Common;

namespace BGCS.Contracts.Interop;

/// <summary>Declares one addressable scalar or record value without inferring pointer direction from its name.</summary>
/// <param name="direction">The native value's input, output or input-output access.</param>
/// <param name="lifetime">The required address validity boundary.</param>
/// <param name="evidence">The declaration of these value semantics.</param>
public sealed record ValueParameterContract(
    NativeParameterDirection direction,
    LifetimeContract lifetime,
    Evidence evidence
)
{
    /// <summary>Gets whether native retains this address, or null when unknown.</summary>
    public bool? retainsPointer { get; init; }
    /// <summary>Gets whether null is permitted; a borrowed managed reference requires false, while a checked constant-null-only binding requires true.</summary>
    public bool? allowsNull { get; init; }
    /// <summary>Gets an optional stricter address alignment than the native pointee type.</summary>
    public int? requiredAlignment { get; init; }
}
