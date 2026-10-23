using System.Collections.Immutable;
using BGCS.Core.Common;

namespace BGCS.Core.Managed;

/// <summary>Customizes an existing native field's managed name and inert documentation without changing storage.</summary>
/// <param name="field">The exact source field name or stable identity.</param>
public sealed record ManagedFieldRuleRequest(string field)
{
    /// <summary>Gets the explicit managed name, or null to retain the naming policy.</summary>
    public string? name { get; init; }
    /// <summary>Gets replacement documentation, or null to retain source prose.</summary>
    public string? documentation { get; init; }
}

/// <summary>Declares a value-only sentinel comparison, independent of native ownership or lifetime.</summary>
/// <param name="field">The native field name or identity.</param>
/// <param name="invalidValue">The typed invalid sentinel.</param>
/// <param name="propertyName">The generated Boolean property name.</param>
public sealed record ManagedRecordValidityRequest(
    string field,
    ManagedDefaultValue invalidValue,
    string propertyName = "IsValid"
);

/// <summary>Customizes one generated native record carrier without changing its ABI layout.</summary>
/// <param name="nativeType">The exact record identity or unambiguous source name.</param>
public sealed record ManagedRecordRuleRequest(string nativeType)
{
    /// <summary>Gets a record name override, or null to retain naming policy.</summary>
    public string? name { get; init; }
    /// <summary>Gets inert replacement record documentation.</summary>
    public string? documentation { get; init; }
    /// <summary>Gets exact field names and documentation overrides.</summary>
    public ImmutableArray<ManagedFieldRuleRequest> fields { get; init; } = [];
    /// <summary>Gets whether to expose the proven native byte size as a constant named NativeSize.</summary>
    public bool emitSizeConstant { get; init; }
    /// <summary>Gets ordered fields assigned by one value-carrier constructor; empty emits no constructor. This never invokes a C++ constructor.</summary>
    public ImmutableArray<string> constructorFields { get; init; } = [];
    /// <summary>Gets an optional explicit field-sentinel validity property.</summary>
    public ManagedRecordValidityRequest? validity { get; init; }
}

/// <summary>Retains a field's inert replacement prose keyed by native identity.</summary>
/// <param name="nativeField">The original field identity.</param>
/// <param name="documentation">The replacement text.</param>
public sealed record ManagedFieldDocumentation(
    StableId nativeField,
    string documentation
);

/// <summary>Retains a bound scalar field comparison without generated source text.</summary>
/// <param name="nativeField">The exact original field.</param>
/// <param name="invalidValue">The typed sentinel.</param>
/// <param name="propertyName">The managed property name.</param>
public sealed record ManagedRecordValidity(
    StableId nativeField,
    ManagedDefaultValue invalidValue,
    string propertyName
);

/// <summary>Preserves optional record-carrier conveniences independently of physical storage.</summary>
public sealed record ManagedRecordPresentation
{
    /// <summary>Gets inert replacement record documentation.</summary>
    public string? documentation { get; init; }
    /// <summary>Gets replacement field documentation.</summary>
    public ImmutableArray<ManagedFieldDocumentation> fieldDocumentation { get; init; } = [];
    /// <summary>Gets whether to expose the independently proven native size.</summary>
    public bool emitSizeConstant { get; init; }
    /// <summary>Gets ordered native field identities assigned by the data constructor.</summary>
    public ImmutableArray<StableId> constructorFields { get; init; } = [];
    /// <summary>Gets a value-only sentinel property.</summary>
    public ManagedRecordValidity? validity { get; init; }
}
