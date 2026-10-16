using System.Collections.Immutable;
using BGCS.Contracts.Common;
using BGCS.Contracts.Native;

namespace BGCS.Contracts.Interop;

/// <summary>Identifies a finite value access protocol without selecting a managed representation.</summary>
public enum NativeValueFamily
{
    /// <summary>A counted sequence of code units with independently declared encoding.</summary>
    String,
    /// <summary>An owning dynamically sized sequence.</summary>
    Vector,
    /// <summary>A borrowed sequence with an independently established owner lifetime.</summary>
    Span,
    /// <summary>A fixed-length owning array.</summary>
    Array,
    /// <summary>A native key and mapped-value collection.</summary>
    Map,
    /// <summary>A native set with no implied managed comparer equivalence.</summary>
    Set,
    /// <summary>An optional native value.</summary>
    Optional,
    /// <summary>A discriminated native value.</summary>
    Variant,
    /// <summary>A native success or error value.</summary>
    Expected,
    /// <summary>A path with an explicit byte conversion protocol.</summary>
    Path,
    /// <summary>A representation and rational duration period.</summary>
    Duration,
    /// <summary>A duration count tied to one native clock identity.</summary>
    TimePoint,
    /// <summary>A unique native owner.</summary>
    UniqueOwner,
    /// <summary>A reference-counted native owner.</summary>
    SharedOwner
}

/// <summary>Identifies one checked native operation; no member name or type spelling drives dispatch.</summary>
public enum NativeValueOperationKind
{
    /// <summary>Constructs a fresh default holder.</summary>
    Construct,
    /// <summary>Returns a fixed-width unsigned count.</summary>
    Count,
    /// <summary>Copies one checked indexed element.</summary>
    GetAt,
    /// <summary>Replaces one checked indexed element.</summary>
    SetAt,
    /// <summary>Appends one copied value.</summary>
    Append,
    /// <summary>Tests value presence.</summary>
    HasValue,
    /// <summary>Copies a present value.</summary>
    GetValue,
    /// <summary>Copies a present error.</summary>
    GetError,
    /// <summary>Returns a discriminant index.</summary>
    Discriminator,
    /// <summary>Copies a specific discriminated alternative.</summary>
    GetAlternative,
    /// <summary>Copies an indexed key.</summary>
    GetKeyAt,
    /// <summary>Copies an indexed mapped value.</summary>
    GetMappedAt,
    /// <summary>Inserts a copied key and value.</summary>
    Insert,
    /// <summary>Returns a pointer with its separate native lifetime contract.</summary>
    GetPointer,
    /// <summary>Creates another shared holder.</summary>
    CloneOwner,
    /// <summary>Transfers a unique holder into another holder.</summary>
    MoveOwner,
    /// <summary>Returns the byte count of an explicit UTF-8 path conversion.</summary>
    PathUtf8Count,
    /// <summary>Copies one checked byte of that path conversion.</summary>
    PathUtf8Byte,
    /// <summary>Returns a native duration representation without changing its period.</summary>
    DurationCount,
    /// <summary>Returns a native count from the declared clock epoch.</summary>
    TimePointCount,
    /// <summary>Copies all string/path units into destination/capacity inputs and returns written units through the checked result channel.</summary>
    CopyCodeUnits
}

/// <summary>References the actual final C export and ordered parameter identities.</summary>
/// <param name="kind">The finite operation meaning.</param>
/// <param name="function">The exact InteropFunction identity.</param>
/// <param name="receiverParameter">The holder receiver, absent only for construction.</param>
/// <param name="arguments">The exact ordered explicit input parameters, excluding receiver, value result and error.</param>
public sealed record NativeValueExport(
    NativeValueOperationKind kind,
    StableId function,
    StableId? receiverParameter,
    ImmutableArray<StableId> arguments
)
{
    /// <summary>Gets the selected variant alternative; null for every other operation.</summary>
    public int? alternativeIndex { get; init; }
}

/// <summary>Retains a value's source identity and independently reparsed C carrier.</summary>
/// <param name="sourceType">The original detached source type identity, retained as provenance.</param>
/// <param name="carrierType">The actual C result carrier; null when the source alternative is void.</param>
/// <param name="isBoolean">Whether the exact source element is bool rather than an arbitrary byte.</param>
public sealed record NativeValueElement(
    StableId sourceType,
    StableId? carrierType,
    bool isBoolean
)
{
    /// <summary>Gets whether this source alternative is void and intentionally has no C value carrier.</summary>
    public bool isVoid { get; init; }
}

/// <summary>Describes counted code units independently from their optional text interpretation.</summary>
/// <param name="codeUnitWidth">The exact source code-unit byte width.</param>
/// <param name="isSigned">Compiler-established code-unit signedness, or null when unavailable.</param>
/// <param name="byteOrder">The target code-unit byte order.</param>
/// <param name="evidence">The code-unit fact provenance.</param>
public sealed record NativeStringValueContract(
    int codeUnitWidth,
    bool? isSigned,
    NativeByteOrder byteOrder,
    Evidence evidence
)
{
    /// <summary>Gets an explicitly declared encoding; basic_string spelling never supplies it.</summary>
    public NativeTextEncoding encoding { get; init; }
    /// <summary>Gets encoding assertion provenance independently from code-unit facts.</summary>
    public Evidence encodingEvidence { get; init; } = new(EvidenceLevel.Unknown, "no-native-string-encoding");
    /// <summary>Gets the counted native length unit.</summary>
    public NativeLengthUnit lengthUnit { get; init; } = NativeLengthUnit.CodeUnits;
    /// <summary>Gets whether embedded zero code units are preserved by counted access.</summary>
    public bool permitsEmbeddedZero { get; init; } = true;
    /// <summary>Gets whether native access scans a declared zero-terminated sequence instead of a native counted member.</summary>
    public bool zeroTerminated { get; init; }
    /// <summary>Gets the explicit finite scan bound; absent for counted access.</summary>
    public int? maximumCodeUnits { get; init; }
    /// <summary>Gets the termination assertion, absent for counted access.</summary>
    public Evidence? terminationEvidence { get; init; }
    /// <summary>Gets whether the actual c_str member is const-qualified; true for ordinary counted string reads.</summary>
    public bool readOnlyReceiver { get; init; } = true;
}

/// <summary>Retains exact sequence extent and storage semantics without inventing contiguous views.</summary>
/// <param name="evidence">The finite sequence protocol provenance.</param>
public sealed record NativeSequenceValueContract(Evidence evidence)
{
    /// <summary>Gets the established fixed element count, or null for dynamic or unproved extent.</summary>
    public ulong? fixedLength { get; init; }
    /// <summary>Gets whether packed Boolean storage is established; false supplies no contiguous-storage guarantee.</summary>
    public bool isPackedBoolean { get; init; }
    /// <summary>Gets whether the represented sequence borrows externally owned elements.</summary>
    public bool isBorrowedView { get; init; }
    /// <summary>Gets a native read lifetime, remaining Unknown without an explicit owner protocol.</summary>
    public LifetimeContract readLifetime { get; init; } = new(NativeLifetimeKind.Unknown, null, new(EvidenceLevel.Unknown, "no-native-sequence-owner"));
}

/// <summary>Identifies the exact native path conversion rather than assuming native character encoding.</summary>
public enum NativePathFormat
{
    /// <summary>UTF-8 with generic path separators produced by generic_u8string.</summary>
    GenericUtf8,
    /// <summary>UTF-8 with native path separators produced by u8string.</summary>
    NativeUtf8
}

/// <summary>Declares a finite native UTF-8 path conversion with counted embedded-zero-preserving access.</summary>
/// <param name="format">The exact conversion operation.</param>
/// <param name="evidence">The conversion contract provenance.</param>
public sealed record NativePathValueContract(
    NativePathFormat format,
    Evidence evidence
);

/// <summary>Stores a normalized exact rational period rather than a floating approximation.</summary>
/// <param name="numerator">The positive compiler-established seconds numerator.</param>
/// <param name="denominator">The positive compiler-established denominator.</param>
/// <param name="evidence">The integral native period fact provenance.</param>
public sealed record NativeDurationPeriod(
    long numerator,
    long denominator,
    Evidence evidence
);

/// <summary>Retains native clock identity without inventing an epoch compatible with managed dates.</summary>
/// <param name="identity">The detached source clock type identity, retained as provenance.</param>
/// <param name="evidence">The native clock identity provenance.</param>
public sealed record NativeClockContract(
    StableId identity,
    Evidence evidence
);

/// <summary>Combines exact C operations with finite source value semantics; ownership remains on individual results.</summary>
/// <param name="id">The stable value protocol identity.</param>
/// <param name="family">The finite source value family.</param>
/// <param name="storageType">The actual opaque C record identity in this InteropModule.</param>
/// <param name="sourceType">The original source type identity, retained as provenance.</param>
/// <param name="operations">Exact checked final C operations.</param>
/// <param name="elements">Source element, key/value, alternative or representation facts in semantic order.</param>
/// <param name="evidence">The lowering contract provenance.</param>
public sealed record NativeValueProtocol(
    StableId id,
    NativeValueFamily family,
    StableId storageType,
    StableId sourceType,
    ImmutableArray<NativeValueExport> operations,
    ImmutableArray<NativeValueElement> elements,
    Evidence evidence
)
{
    /// <summary>Gets exact available holder cleanup; its presence does not establish cleanup thread independence.</summary>
    public StableId? releaseExport { get; init; }
    /// <summary>Gets an optional exact standard-value creating-call cleanup capability; absent for unproved allocator or custom type semantics.</summary>
    public NativeValueReleaseContract? scopedRelease { get; init; }
    /// <summary>Gets code-unit facts only for a string value.</summary>
    public NativeStringValueContract? text { get; init; }
    /// <summary>Gets sequence facts for string, vector, span, array, map or set.</summary>
    public NativeSequenceValueContract? sequence { get; init; }
    /// <summary>Gets the exact path conversion only for a path value.</summary>
    public NativePathValueContract? path { get; init; }
    /// <summary>Gets the exact duration period when independently established; null withholds unit conversion.</summary>
    public NativeDurationPeriod? period { get; init; }
    /// <summary>Gets the original clock only for time-point values; it carries no known-epoch claim.</summary>
    public NativeClockContract? clock { get; init; }
}
