using BGCS.Contracts.Common;

namespace BGCS.Contracts.Interop;

/// <summary>Describes native ownership independently of managed wrapper selection.</summary>
public enum NativeOwnershipKind
{
    /// <summary>The owner and release responsibility are unknown.</summary>
    Unknown,
    /// <summary>The caller borrows a resource and may not release it.</summary>
    Borrowed,
    /// <summary>The caller receives responsibility for releasing the resource.</summary>
    Owned,
    /// <summary>A call transfers ownership to the receiving side.</summary>
    Transferred
}

/// <summary>Describes the resource release domain without storing an executable release delegate.</summary>
/// <param name="kind">The native ownership responsibility.</param>
/// <param name="lifetime">The resource validity boundary.</param>
/// <param name="evidence">The source of ownership facts.</param>
public sealed record OwnershipContract(
    NativeOwnershipKind kind,
    LifetimeContract lifetime,
    Evidence evidence
)
{
    /// <summary>Gets the exact C ABI release export identity.</summary>
    public StableId? releaseFunction { get; init; }
    /// <summary>Gets the allocator domain identity matched by allocation and release.</summary>
    public StableId? allocator { get; init; }
    /// <summary>Gets the declared release thread domain; null means no thread-domain fact is available.</summary>
    public string? releaseThreadDomain { get; init; }
    /// <summary>Gets explicit native address retention for borrowed input protocols, or null when unknown.</summary>
    public bool? retainsPointer { get; init; }
}
