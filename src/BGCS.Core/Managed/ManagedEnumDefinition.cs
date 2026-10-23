using System.Collections.Immutable;

namespace BGCS.Core.Managed;

/// <summary>Names a fixed-width managed integral representation independently of backend syntax.</summary>
public enum ManagedIntegralKind
{
    /// <summary>No integral representation has been explicitly declared.</summary>
    Unknown,
    /// <summary>A signed eight-bit integer.</summary>
    Int8,
    /// <summary>An unsigned eight-bit integer.</summary>
    UInt8,
    /// <summary>A signed sixteen-bit integer.</summary>
    Int16,
    /// <summary>An unsigned sixteen-bit integer.</summary>
    UInt16,
    /// <summary>A signed thirty-two-bit integer.</summary>
    Int32,
    /// <summary>An unsigned thirty-two-bit integer.</summary>
    UInt32,
    /// <summary>A signed sixty-four-bit integer.</summary>
    Int64,
    /// <summary>An unsigned sixty-four-bit integer.</summary>
    UInt64
}

/// <summary>Declares a managed enum without changing the native raw calling representation.</summary>
/// <param name="name">The public managed type name.</param>
/// <param name="underlyingType">The fixed-width managed integral representation.</param>
/// <param name="values">Explicit decimal integral values, preserving all sixty-four bits.</param>
public sealed record ManagedEnumDefinition(
    string name,
    ManagedIntegralKind underlyingType,
    ImmutableArray<ManagedEnumValue> values
)
{
    /// <summary>Gets inert plain-text documentation for this managed declaration, or null to omit it.</summary>
    public string? documentation { get; init; }
    /// <summary>Gets whether the public enum describes independently combinable flags.</summary>
    public bool isFlags { get; init; }
}

/// <summary>Declares one integral enum member without embedding a source-language expression.</summary>
/// <param name="name">The public managed member name.</param>
/// <param name="value">The complete signed or unsigned decimal integer.</param>
public sealed record ManagedEnumValue(
    string name,
    string value
)
{
    /// <summary>Gets inert plain-text documentation for this managed constant, or null to omit it.</summary>
    public string? documentation { get; init; }
}
