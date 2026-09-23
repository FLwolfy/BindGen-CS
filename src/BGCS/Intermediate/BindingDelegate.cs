namespace BGCS.Intermediate;

/// <summary>Represents a native function-pointer contract independently of C# source emission.</summary>
public sealed class BindingDelegate
{
    public BindingDelegate(string nativeName, string managedName, BindingTypeReference returnType,
        MarshallingPlan returnMarshalling)
    {
        NativeName = nativeName;
        ManagedName = managedName;
        ReturnType = returnType;
        ReturnMarshalling = returnMarshalling;
    }

    public string NativeName { get; }
    public string ManagedName { get; }
    public BindingTypeReference ReturnType { get; }
    public MarshallingPlan ReturnMarshalling { get; }
    public string CallingConvention { get; init; } = "Cdecl";
    public IList<BindingParameter> Parameters { get; } = new List<BindingParameter>();
}
