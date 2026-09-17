namespace BGCS.Intermediate;

/// <summary>
/// Identifies data flow across a native call boundary.
/// </summary>
public enum BindingDirection
{
    In,
    Out,
    InOut
}

/// <summary>
/// Identifies the semantic role of a callable binding.
/// </summary>
public enum BindingFunctionKind
{
    Free,
    Instance,
    Static,
    Constructor,
    Destructor
}

/// <summary>
/// Describes one analyzed callable parameter.
/// </summary>
/// <param name="NativeName">Native parameter name.</param>
/// <param name="ManagedName">Managed parameter name.</param>
/// <param name="Type">Analyzed parameter type.</param>
/// <param name="Direction">Interop data direction.</param>
/// <param name="Marshalling">Conversion and ownership plan.</param>
public sealed record BindingParameter(string NativeName, string ManagedName, BindingTypeReference Type,
    BindingDirection Direction, MarshallingPlan Marshalling);

/// <summary>
/// Represents one callable in the binding intermediate representation.
/// </summary>
public sealed class BindingFunction
{
    public BindingFunction(string nativeName, string managedName, BindingFunctionKind kind,
        BindingTypeReference returnType, MarshallingPlan returnMarshalling)
    {
        NativeName = nativeName;
        ManagedName = managedName;
        Kind = kind;
        ReturnType = returnType;
        ReturnMarshalling = returnMarshalling;
    }

    public string NativeName { get; }
    public string ManagedName { get; }
    public BindingFunctionKind Kind { get; }
    public BindingTypeReference ReturnType { get; }
    public MarshallingPlan ReturnMarshalling { get; }
    public string CallingConvention { get; init; } = "Cdecl";
    public bool IsVariadic { get; init; }
    public string? DeclaringType { get; init; }
    public IList<BindingParameter> Parameters { get; } = new List<BindingParameter>();
}
