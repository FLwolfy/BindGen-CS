using System.Collections.Generic;
using System.Linq;
using BGCS.Intermediate;

namespace BGCS.Analysis;

internal sealed class BindingFunctionBuilder
{
    internal BindingFunctionBuilder(
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

    public string nativeName { get; }
    public string managedName { get; }
    public string rawManagedName { get; set; } = string.Empty;
    public BindingFunctionKind kind { get; }
    public BindingTypeReference returnType { get; }
    public MarshallingPlan returnMarshalling { get; }
    public string callingConvention { get; set; } = "Cdecl";
    public bool isVariadic { get; set; }
    public string? declaringType { get; set; }
    /// <summary>Managed static class that owns public wrappers. Native entry points remain on the module API class.</summary>
    public string? managedContainer { get; set; }
    public BindingManagedFunctionKind managedKind { get; set; }
    public string? managedReceiverType { get; set; }
    public int? managedReceiverIndex { get; set; }
    public int? functionTableIndex { get; set; }
    /// <summary>Safety analysis rejected inferred friendly overloads; the raw ABI remains available.</summary>
    public bool suppressFriendlySurface { get; set; }
    public List<BindingParameter> parameters { get; set; } = [];

    internal BindingFunction Freeze()
    {
        return new(this.nativeName, this.managedName, this.kind, this.returnType, this.returnMarshalling)
        {
            rawManagedName = this.rawManagedName,
            callingConvention = this.callingConvention,
            isVariadic = this.isVariadic,
            declaringType = this.declaringType,
            managedContainer = this.managedContainer,
            managedKind = this.managedKind,
            managedReceiverType = this.managedReceiverType,
            managedReceiverIndex = this.managedReceiverIndex,
            functionTableIndex = this.functionTableIndex,
            suppressFriendlySurface = this.suppressFriendlySurface,
            parameters = this.parameters
        };
    }

    internal static BindingFunctionBuilder From(BindingFunction model)
    {
        return new(model.nativeName, model.managedName, model.kind, model.returnType, model.returnMarshalling)
        {
            rawManagedName = model.rawManagedName,
            callingConvention = model.callingConvention,
            isVariadic = model.isVariadic,
            declaringType = model.declaringType,
            managedContainer = model.managedContainer,
            managedKind = model.managedKind,
            managedReceiverType = model.managedReceiverType,
            managedReceiverIndex = model.managedReceiverIndex,
            functionTableIndex = model.functionTableIndex,
            suppressFriendlySurface = model.suppressFriendlySurface,
            parameters = model.parameters.ToList()
        };
    }
}
