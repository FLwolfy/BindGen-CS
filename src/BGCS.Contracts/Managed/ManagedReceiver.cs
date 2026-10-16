using BGCS.Contracts.Common;

namespace BGCS.Contracts.Managed;

/// <summary>Selects a resource wrapper's independently established native lifetime protocol.</summary>
public enum ManagedReceiverKind
{
    /// <summary>The wrapper owns a native resource with a matching release operation.</summary>
    Owned,
    /// <summary>The wrapper only retains a module whose lifetime bounds an explicitly borrowed address.</summary>
    ModuleBorrowed,
    /// <summary>The receiver is the caller's original generated value carrier, borrowed only for the call.</summary>
    Value
}

/// <summary>Selects an exact C callable receiver without changing its native ABI or inventing a C++ source owner.</summary>
/// <param name="nativeType">The record or named opaque-pointer alias whose identity must match the receiver.</param>
/// <param name="parameter">The exact native receiver parameter.</param>
/// <param name="objectName">The public wrapper or existing value-carrier name.</param>
public sealed record ManagedReceiver(
    StableId nativeType,
    StableId parameter,
    string objectName
)
{
    /// <summary>Gets the native lifetime protocol required from a matching factory.</summary>
    public ManagedReceiverKind kind { get; init; }
    /// <summary>Gets an explicitly required allocation/release domain, or null when the receiver has no domain restriction.</summary>
    public StableId? requiredReleaseDomain { get; init; }
}

/// <summary>Requests an explicit C function as a member of a resource wrapper or generated value carrier.</summary>
/// <param name="function">The exact export, source identity or unambiguous source callable.</param>
/// <param name="nativeType">The exact native record or named opaque-pointer alias.</param>
/// <param name="receiver">The native receiver parameter name or identity.</param>
public sealed record ManagedReceiverRequest(
    string function,
    string nativeType,
    string receiver
)
{
    /// <summary>Gets the required wrapper lifetime protocol; module borrowing never supplies a native destructor.</summary>
    public ManagedReceiverKind kind { get; init; }
    /// <summary>Gets a public wrapper name, or null to use the managed native type name plus Object.</summary>
    public string? objectName { get; init; }
    /// <summary>Gets an optional member name replacing the operation's ordinary managed name.</summary>
    public string? name { get; init; }
    /// <summary>Gets an optional configured allocation/release domain required by this member.</summary>
    public string? releaseDomain { get; init; }
}
