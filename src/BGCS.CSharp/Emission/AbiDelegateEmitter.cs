using System.Linq;
using System.Text;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

internal static class AbiDelegateEmitter
{
    internal static string Emit(CSharpAbiDelegatePlan declaration)
    {
        StringBuilder text = new();
        DocumentationProjector.Append(text, declaration.documentation);
        text.Append("[global::System.Runtime.InteropServices.UnmanagedFunctionPointer(global::System.Runtime.InteropServices.CallingConvention.")
            .Append(declaration.callingConvention switch { "Stdcall" => "StdCall", "Fastcall" => "FastCall", "Thiscall" => "ThisCall", _ => "Cdecl" }).AppendLine(")]")
            .Append("public unsafe delegate ").Append(declaration.returnType).Append(' ').Append(declaration.name).Append('(')
            .Append(string.Join(", ", declaration.parameterTypes.Select((
                type,
                index
            ) => type + " argument" + index))).AppendLine(");");
        return text.ToString();
    }
}
