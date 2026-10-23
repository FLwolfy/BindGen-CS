using System.Collections.Immutable;
using BGCS.Core.Common;

namespace BGCS.Core.Managed;

/// <summary>Projects a native record by copying fields through its independently verified generated raw carrier.</summary>
/// <param name="nativeType">The exact native record identity.</param>
/// <param name="managedType">The closed consumer type, including any generic arguments.</param>
/// <param name="fields">A complete one-to-one mapping of ordinary native fields to public writable managed fields.</param>
public sealed record ExternalTypeProjection(
    StableId nativeType,
    string managedType,
    ImmutableArray<ExternalFieldProjection> fields
)
{
    /// <summary>Gets optional independently supplied managed storage observations; BGCS does not execute a consumer layout probe.</summary>
    public ManagedTypeStorageFact? managedStorage { get; init; }
    /// <summary>Gets statically bound scalar field semantics for copying through a separate raw carrier, without asserting CLR storage layout.</summary>
    public ManagedStaticTypeShape? managedShape { get; init; }
}

/// <summary>Describes statically resolved unmanaged value fields without offsets, alignment or runtime layout assertions.</summary>
/// <param name="managedType">The exact configured closed consumer type.</param>
/// <param name="fields">Every ordinary instance field with compiler-established scalar semantics.</param>
/// <param name="evidence">Declared static binding provenance; this is not a verified runtime layout receipt.</param>
public sealed record ManagedStaticTypeShape(
    string managedType,
    ImmutableArray<ManagedStaticFieldShape> fields,
    Evidence evidence
);

/// <summary>Describes a scalar field for ordinary value assignment or address-bit copying without storage offsets.</summary>
/// <param name="name">The exact consumer field name.</param>
/// <param name="typeIdentity">The resolved scalar type identity.</param>
/// <param name="scalarKind">The scalar conversion semantics.</param>
/// <param name="size">The scalar byte width for the declared target; not an enclosing CLR layout measurement.</param>
/// <param name="isPublic">Whether the field is publicly accessible.</param>
/// <param name="isWritable">Whether the instance field is writable.</param>
public sealed record ManagedStaticFieldShape(
    string name,
    string typeIdentity,
    ManagedScalarKind scalarKind,
    int size,
    bool isPublic,
    bool isWritable
);

/// <summary>Associates exact source identities with managed fields without depending on generated raw field names.</summary>
/// <param name="nativeField">The native declaration identity.</param>
/// <param name="managedField">The public writable consumer field name.</param>
public sealed record ExternalFieldProjection(
    StableId nativeField,
    string managedField
);

/// <summary>Identifies scalar CLR field semantics independently of output-language spelling.</summary>
public enum ManagedScalarKind
{
    /// <summary>A non-scalar or unsupported representation.</summary>
    Unknown,
    /// <summary>A signed integral scalar.</summary>
    SignedInteger,
    /// <summary>An unsigned integral scalar.</summary>
    UnsignedInteger,
    /// <summary>An IEEE binary floating-point scalar.</summary>
    BinaryFloat,
    /// <summary>A data address stored as an unmanaged pointer, IntPtr or UIntPtr; copying it grants no pointee lifetime.</summary>
    NativeAddress
}

/// <summary>Records actual CLR field storage measured in an isolated consumer process.</summary>
/// <param name="name">The actual managed field name.</param>
/// <param name="typeIdentity">The closed runtime field type identity.</param>
/// <param name="scalarKind">The observed scalar semantics.</param>
/// <param name="size">The actual CLR field byte size.</param>
/// <param name="offset">The actual byte offset within the managed value.</param>
/// <param name="isPublic">Whether the field is public.</param>
/// <param name="isWritable">Whether the field is writable after default initialization.</param>
public sealed record ManagedFieldStorageFact(
    string name,
    string typeIdentity,
    ManagedScalarKind scalarKind,
    int size,
    long offset,
    bool isPublic,
    bool isWritable
);

/// <summary>Identifies the runtime and frozen compilation inputs that produced managed storage observations.</summary>
/// <param name="frameworkDescription">The actual executing framework.</param>
/// <param name="runtimeIdentifier">The actual runtime identifier.</param>
/// <param name="processArchitecture">The actual process CPU architecture.</param>
/// <param name="pointerSize">The executing CLR pointer width.</param>
/// <param name="isLittleEndian">The executing CLR byte order.</param>
/// <param name="inputDigest">The content digest of the captured probe and consumer compilation inputs.</param>
public sealed record ManagedProbeIdentity(
    string frameworkDescription,
    string runtimeIdentifier,
    string processArchitecture,
    int pointerSize,
    bool isLittleEndian,
    ContentDigest inputDigest
);

/// <summary>Preserves measured CLR storage without asserting native calling-convention compatibility.</summary>
/// <param name="managedType">The normalized closed consumer type syntax.</param>
/// <param name="runtimeTypeIdentity">The actual closed CLR type identity.</param>
/// <param name="size">The actual managed value byte size.</param>
/// <param name="alignment">The actual managed field alignment measured using a byte-prefix envelope.</param>
/// <param name="isReferenceFree">Whether the actual value contains no managed references.</param>
/// <param name="layoutKind">The CLR layout kind observed by the consumer.</param>
/// <param name="fields">Every instance field, including inaccessible fields.</param>
/// <param name="probe">The actual consumer execution identity.</param>
/// <param name="evidence">The independent storage observation receipt.</param>
public sealed record ManagedTypeStorageFact(
    string managedType,
    string runtimeTypeIdentity,
    int size,
    int alignment,
    bool isReferenceFree,
    string layoutKind,
    ImmutableArray<ManagedFieldStorageFact> fields,
    ManagedProbeIdentity probe,
    Evidence evidence
);
