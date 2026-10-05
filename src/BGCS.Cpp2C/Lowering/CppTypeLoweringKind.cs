namespace BGCS.Cpp2C.Lowering;

/// <summary>Semantic category exposed to analyzers, bridge emitters, and managed projections.</summary>
public enum CppTypeLoweringKind
{
    /// <summary>
    /// An extension-defined semantic category.
    /// </summary>
    Custom,
    /// <summary>
    /// A UTF-8 string value or view.
    /// </summary>
    Utf8String,
    /// <summary>
    /// An exclusively owned native object.
    /// </summary>
    UniqueOwner,
    /// <summary>
    /// A reference-counted native object.
    /// </summary>
    SharedOwner,
    /// <summary>
    /// A borrowed contiguous view.
    /// </summary>
    Span,
    /// <summary>
    /// A contiguous native container.
    /// </summary>
    Vector,
    /// <summary>
    /// A value with explicit presence.
    /// </summary>
    Optional,
    /// <summary>
    /// A fixed-size contiguous container.
    /// </summary>
    Array,
    /// <summary>
    /// A key/value native container.
    /// </summary>
    Map,
    /// <summary>
    /// A unique-element native container.
    /// </summary>
    Set,
    /// <summary>
    /// A discriminated native union.
    /// </summary>
    Variant,
    /// <summary>
    /// A native value-or-error result.
    /// </summary>
    Expected,
    /// <summary>
    /// A filesystem path value.
    /// </summary>
    Path,
    /// <summary>
    /// A clock duration value.
    /// </summary>
    ChronoDuration,
    /// <summary>
    /// A clock time-point value.
    /// </summary>
    ChronoTimePoint
}
