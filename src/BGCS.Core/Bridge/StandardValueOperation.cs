using System.Collections.Immutable;
using BGCS.Core.Common;

namespace BGCS.Core.Bridge;

/// <summary>Identifies a standard-library value family independently of its compiler implementation.</summary>
public enum StandardValueKind
{
    /// <summary>A basic_string value with no assumed text encoding.</summary>
    String,
    /// <summary>A vector, including the packed vector&lt;bool&gt; specialization.</summary>
    Vector,
    /// <summary>A borrowed span whose underlying storage remains externally owned.</summary>
    Span,
    /// <summary>A fixed array value.</summary>
    Array,
    /// <summary>An ordered map.</summary>
    Map,
    /// <summary>An ordered set.</summary>
    Set,
    /// <summary>An optional value.</summary>
    Optional,
    /// <summary>A discriminated variant.</summary>
    Variant,
    /// <summary>An expected success or error value.</summary>
    Expected,
    /// <summary>A filesystem path accessed through explicit UTF-8 conversion.</summary>
    Path,
    /// <summary>A duration preserving its native representation and period.</summary>
    Duration,
    /// <summary>A time point preserving its clock and duration.</summary>
    TimePoint,
    /// <summary>A unique_ptr holder preserving unique native ownership.</summary>
    UniquePointer,
    /// <summary>A shared_ptr holder preserving the native reference count.</summary>
    SharedPointer
}

/// <summary>Identifies one checked access, mutation or ownership action on a standard-library holder.</summary>
public enum StandardValueAction
{
    /// <summary>Constructs a default value; the native compiler checks constructibility.</summary>
    Construct,
    /// <summary>Returns a count using a fixed-width unsigned C scalar.</summary>
    Count,
    /// <summary>Copies an indexed value, with a native range check before access.</summary>
    GetAt,
    /// <summary>Replaces an indexed value, with a native range check before access.</summary>
    SetAt,
    /// <summary>Appends one copied value.</summary>
    Append,
    /// <summary>Returns whether an optional, expected or pointer holder contains a value.</summary>
    HasValue,
    /// <summary>Copies the present value, or reports a native boundary error.</summary>
    GetValue,
    /// <summary>Copies an expected error value, or reports a native boundary error.</summary>
    GetError,
    /// <summary>Returns the active variant alternative index.</summary>
    Discriminator,
    /// <summary>Copies one explicitly selected variant alternative.</summary>
    GetAlternative,
    /// <summary>Copies the key at a checked ordered map position.</summary>
    GetKeyAt,
    /// <summary>Copies the value at a checked ordered map position.</summary>
    GetMappedAt,
    /// <summary>Inserts or replaces one copied key and mapped value.</summary>
    Insert,
    /// <summary>Returns a borrowed smart-pointer pointee tied to the holder lifetime.</summary>
    GetPointer,
    /// <summary>Creates another shared holder through its native copy constructor.</summary>
    CloneShared,
    /// <summary>Moves a unique holder's ownership into a newly allocated holder.</summary>
    MoveUnique,
    /// <summary>Returns an explicit UTF-8 path byte count.</summary>
    PathUtf8Count,
    /// <summary>Returns one checked byte of an explicit UTF-8 path conversion.</summary>
    PathUtf8Byte,
    /// <summary>Returns the native duration representation without changing its period.</summary>
    DurationCount,
    /// <summary>Returns the native duration representation measured from the declared clock epoch.</summary>
    TimePointCount,
    /// <summary>Copies a complete counted string/path into exact C code-unit carriers and returns the written code-unit count.</summary>
    CopyCodeUnits
}

/// <summary>Describes a typed generated operation on a source-language standard-library value.</summary>
/// <param name="family">The verified source-language family.</param>
/// <param name="action">The finite source-language operation.</param>
/// <param name="argumentTypes">The source-language element, key, mapped or alternative identities in semantic order.</param>
public sealed record StandardValueOperation(
    StandardValueKind family,
    StandardValueAction action,
    ImmutableArray<StableId> argumentTypes
)
{
    /// <summary>Gets the explicitly selected variant alternative; null for other operations.</summary>
    public int? alternativeIndex { get; init; }
}
