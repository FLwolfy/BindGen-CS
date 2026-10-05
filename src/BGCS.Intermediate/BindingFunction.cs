using System.Collections.Generic;

namespace BGCS.Intermediate;

/// <summary>
/// Identifies data flow across a native call boundary.
/// </summary>
public enum BindingDirection
{
    /// <summary>
    /// Data flows into the native call.
    /// </summary>
    In,
    /// <summary>
    /// Data flows out of the native call.
    /// </summary>
    Out,
    /// <summary>
    /// The native call reads and modifies the same data.
    /// </summary>
    InOut
}

/// <summary>
/// Identifies the semantic role of a callable binding.
/// </summary>
public enum BindingFunctionKind
{
    /// <summary>
    /// A native callable without an owner type.
    /// </summary>
    Free,
    /// <summary>
    /// A method requiring a native object receiver.
    /// </summary>
    Instance,
    /// <summary>
    /// A type-scoped callable without an object receiver.
    /// </summary>
    Static,
    /// <summary>
    /// An operation that creates a native object.
    /// </summary>
    Constructor,
    /// <summary>
    /// An operation that releases a native object.
    /// </summary>
    Destructor
}

/// <summary>
/// Describes one analyzed callable parameter.
/// </summary>
/// <param name = "nativeName">Native parameter name.</param>
/// <param name = "managedName">Managed parameter name.</param>
/// <param name = "type">Analyzed parameter type.</param>
/// <param name = "direction">Interop data direction.</param>
/// <param name = "marshalling">Conversion and ownership plan.</param>
public sealed record BindingParameter(
    string nativeName,
    string managedName,
    BindingTypeReference type,
    BindingDirection direction,
    MarshallingPlan marshalling
)
{
    /// <summary>
    /// Gets the normalized optional argument expression, or null when no default is declared.
    /// </summary>
    public string? defaultValue { get; init; }
}

/// <summary>Identifies the public managed presentation of a native free function.</summary>
public enum BindingManagedFunctionKind
{
    /// <summary>
    /// A static managed wrapper.
    /// </summary>
    Static,
    /// <summary>
    /// A managed extension wrapper with an explicit receiver.
    /// </summary>
    Extension,
    /// <summary>
    /// A wrapper declared on a managed instance.
    /// </summary>
    Instance
}

/// <summary>
/// Represents one callable in the binding intermediate representation.
/// </summary>
public sealed record BindingFunction
{
    private IReadOnlyList<BindingParameter> m_parameters = [];
    /// <summary>
    /// Captures a native callable and its analyzed return contract.
    /// </summary>
    /// <param name="nativeName">
    /// Native entry point.
    /// </param>
    /// <param name="managedName">
    /// Public managed callable name.
    /// </param>
    /// <param name="kind">
    /// Native callable role.
    /// </param>
    /// <param name="returnType">
    /// Analyzed return type.
    /// </param>
    /// <param name="returnMarshalling">
    /// Return conversion, ownership, and cleanup plan.
    /// </param>
    public BindingFunction(
        string nativeName,
        string managedName,
        BindingFunctionKind kind,
        BindingTypeReference returnType,
        MarshallingPlan returnMarshalling
    ) {
        this.nativeName = nativeName;
        this.managedName = managedName;
        this.kind = kind;
        this.returnType = returnType;
        this.returnMarshalling = returnMarshalling;
    }

    /// <summary>
    /// Gets the native entry point name.
    /// </summary>
    public string nativeName { get; }
    /// <summary>
    /// Gets the public managed callable name.
    /// </summary>
    public string managedName { get; }
    /// <summary>
    /// Gets the managed raw ABI method name before friendly overloads.
    /// </summary>
    public string rawManagedName { get; init; } = string.Empty;
    /// <summary>
    /// Gets the native callable role, including construction and destruction.
    /// </summary>
    public BindingFunctionKind kind { get; }
    /// <summary>
    /// Gets the analyzed target ABI return type.
    /// </summary>
    public BindingTypeReference returnType { get; }
    /// <summary>
    /// Gets the return conversion, ownership, and cleanup plan.
    /// </summary>
    public MarshallingPlan returnMarshalling { get; }
    /// <summary>
    /// Gets the unmanaged calling convention required by this entry point.
    /// </summary>
    public string callingConvention { get; init; } = "Cdecl";
    /// <summary>
    /// Gets whether the native callable accepts a variable argument tail.
    /// </summary>
    public bool isVariadic { get; init; }
    /// <summary>
    /// Gets the native owner type, or null for a free function.
    /// </summary>
    public string? declaringType { get; init; }
    /// <summary>Managed static class that owns public wrappers. Native entry points remain on the module API class.</summary>
    public string? managedContainer { get; init; }
    /// <summary>
    /// Gets the public wrapper presentation selected during analysis.
    /// </summary>
    public BindingManagedFunctionKind managedKind { get; init; }
    /// <summary>
    /// Gets the managed receiver type for extension or instance wrappers, or null when absent.
    /// </summary>
    public string? managedReceiverType { get; init; }
    /// <summary>
    /// Gets the native parameter supplying the managed receiver, or null when absent.
    /// </summary>
    public int? managedReceiverIndex { get; init; }
    /// <summary>
    /// Gets the deterministic native symbol slot, or null for direct imports.
    /// </summary>
    public int? functionTableIndex { get; init; }
    /// <summary>Safety analysis rejected inferred friendly overloads; the raw ABI remains available.</summary>
    public bool suppressFriendlySurface { get; init; }
    /// <summary>
    /// Gets a frozen copy of parameters in native call order.
    /// </summary>
    public IReadOnlyList<BindingParameter> parameters { get => m_parameters; init => m_parameters = BindingCollection.Copy(value); }
}
