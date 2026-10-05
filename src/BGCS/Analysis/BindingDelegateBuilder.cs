using System.Collections.Generic;
using System.Linq;
using BGCS.Intermediate;

namespace BGCS.Analysis;

internal sealed class BindingDelegateBuilder
{
    internal BindingDelegateBuilder(
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

    public string nativeName { get; }
    public string managedName { get; }
    public BindingTypeReference returnType { get; }
    public MarshallingPlan returnMarshalling { get; }
    public string callingConvention { get; set; } = "Cdecl";
    public List<BindingParameter> parameters { get; set; } = [];

    internal BindingDelegate Freeze()
    {
        return new(this.nativeName, this.managedName, this.returnType, this.returnMarshalling)
        {
            callingConvention = this.callingConvention,
            parameters = this.parameters
        };
    }

    internal static BindingDelegateBuilder From(BindingDelegate model)
    {
        return new(model.nativeName, model.managedName, model.returnType, model.returnMarshalling)
        {
            callingConvention = model.callingConvention,
            parameters = model.parameters.ToList()
        };
    }

    public static implicit operator BindingDelegate(BindingDelegateBuilder builder) => builder.Freeze();
}
