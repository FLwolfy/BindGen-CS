using BGCS.Contracts.Common;

namespace BGCS.Contracts.Interop;

/// <summary>Describes native buffer bounds independently of managed span or array projection.</summary>
/// <param name="elementType">The native buffer element identity.</param>
/// <param name="lengthUnit">The unit used by native length and capacity.</param>
/// <param name="direction">Native storage access.</param>
/// <param name="lifetime">The storage validity boundary.</param>
/// <param name="evidence">The source of buffer facts.</param>
public sealed record BufferContract(
    StableId elementType,
    NativeLengthUnit lengthUnit,
    NativeParameterDirection direction,
    LifetimeContract lifetime,
    Evidence evidence
)
{
    /// <summary>Gets the native length parameter identity.</summary>
    public StableId? lengthParameter { get; init; }
    /// <summary>Gets the native capacity parameter identity.</summary>
    public StableId? capacityParameter { get; init; }
    /// <summary>Gets an output integral-count pointer whose written extent uses the same declared units; null means no written-count protocol.</summary>
    public StableId? writtenCountParameter { get; init; }
    /// <summary>Gets address retention, or null when unknown.</summary>
    public bool? retainsPointer { get; init; }
    /// <summary>Gets null allowance, or null when unknown.</summary>
    public bool? allowsNull { get; init; }
    /// <summary>Gets a declared native allocation alignment, or null when unknown.</summary>
    public int? requiredAlignment { get; init; }
}
