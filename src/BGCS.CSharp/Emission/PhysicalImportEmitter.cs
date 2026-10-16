using System.Linq;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

internal static class PhysicalImportEmitter
{
    internal static string FunctionPointer(
        CSharpMethodPlan method,
        CSharpPhysicalImportPlan? physical
    )
        => "delegate* unmanaged[" + method.callingConvention + "]<"
            + string.Join(", ", (physical?.parameterTypes ?? method.parameterTypes).Append(physical?.returnType ?? method.returnType)) + ">";

    internal static string Forwarder(
        CSharpEmissionPlan plan,
        CSharpPhysicalImportPlan physical,
        bool table
    ) {
        CSharpMethodPlan method = plan.methods[physical.methodIndex];
        string name = table ? "m_function" + physical.methodIndex : "Function" + physical.methodIndex;
        string target = table ? "this." + physical.fieldName : physical.declarationName;
        string arguments = string.Join(", ", Enumerable.Range(0, method.parameterTypes.Length)
            .Select(index => (physical.addressParameters.Contains(index) ? "(nint)" : string.Empty) + "arg" + index));
        string returned = physical.addressResult ? "(" + method.returnType + ")" : string.Empty;
        return "    " + (table ? "private " : "internal static ") + method.returnType + " " + name + "("
            + string.Join(", ", method.parameterTypes.Select((
                type,
                index
            ) => type + " arg" + index)) + ")\n    {\n        "
            + (method.returnType == "void" ? string.Empty : "return " + returned) + target + "(" + arguments + ");\n    }\n";
    }
}
