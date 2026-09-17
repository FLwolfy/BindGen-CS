namespace BGCS.Intermediate;

/// <summary>
/// Identifies the generated marshalling strategy for a parameter or return value.
/// </summary>
public enum MarshallingStrategy
{
    Blittable,
    Pointer,
    Handle,
    String,
    Span,
    Callback,
    Optional,
    Custom
}

/// <summary>
/// Identifies native memory ownership at an interop boundary.
/// </summary>
public enum BindingOwnership
{
    Unknown,
    Borrowed,
    Shared,
    Transferred,
    Owned,
    CallerAllocated
}

/// <summary>
/// Identifies the character encoding used by a native string.
/// </summary>
public enum BindingStringEncoding
{
    None,
    Ansi,
    Utf8,
    Utf16,
    Utf32
}

/// <summary>
/// Defines a complete native-to-managed conversion plan consumed by emitters.
/// </summary>
/// <param name="Strategy">Conversion strategy.</param>
/// <param name="Ownership">Memory ownership contract.</param>
/// <param name="StringEncoding">String encoding when applicable.</param>
/// <param name="LengthParameter">Related element-count parameter, when known.</param>
/// <param name="CapacityParameter">Related capacity parameter, when known.</param>
/// <param name="WrittenCountParameter">Related output-count parameter, when known.</param>
/// <param name="RequiresCleanup">Whether generated code must execute cleanup after the native call.</param>
/// <param name="CleanupFunction">Native cleanup function for an owned value, when configured.</param>
/// <param name="NullTerminated">Whether a string or sequence uses a terminating zero element.</param>
public sealed record MarshallingPlan(MarshallingStrategy Strategy, BindingOwnership Ownership,
    BindingStringEncoding StringEncoding = BindingStringEncoding.None, string? LengthParameter = null,
    string? CapacityParameter = null, string? WrittenCountParameter = null, bool RequiresCleanup = false,
    string? CleanupFunction = null, bool NullTerminated = false);
