using System.Collections.Generic;

namespace BGCS.Intermediate;

/// <summary>Represents a native function-pointer contract independently of C# source emission.</summary>
public sealed record BindingDelegate
{
    private IReadOnlyList<BindingParameter> m_parameters = [];
    /// <summary>
    /// Captures a callback signature independently of the output language.
    /// </summary>
    /// <param name="nativeName">
    /// Native function-pointer declaration name.
    /// </param>
    /// <param name="managedName">
    /// Managed callback type name.
    /// </param>
    /// <param name="returnType">
    /// Analyzed native callback return type.
    /// </param>
    /// <param name="returnMarshalling">
    /// Callback return conversion and ownership plan.
    /// </param>
    public BindingDelegate(
        string nativeName,
        string managedName,
        BindingTypeReference returnType,
        MarshallingPlan returnMarshalling
    ) {
        this.nativeName = nativeName;
        this.managedName = managedName;
        this.returnType = returnType;
        this.returnMarshalling = returnMarshalling;
    }

    /// <summary>
    /// Gets the native function-pointer declaration name.
    /// </summary>
    public string nativeName { get; }
    /// <summary>
    /// Gets the managed callback type name.
    /// </summary>
    public string managedName { get; }
    /// <summary>
    /// Gets the analyzed native callback return type.
    /// </summary>
    public BindingTypeReference returnType { get; }
    /// <summary>
    /// Gets the callback return conversion and ownership plan.
    /// </summary>
    public MarshallingPlan returnMarshalling { get; }
    /// <summary>
    /// Gets the unmanaged calling convention used when native code invokes the callback.
    /// </summary>
    public string callingConvention { get; init; } = "Cdecl";
    /// <summary>
    /// Gets a frozen copy of callback parameters in native call order.
    /// </summary>
    public IReadOnlyList<BindingParameter> parameters { get => m_parameters; init => m_parameters = BindingCollection.Copy(value); }
}
