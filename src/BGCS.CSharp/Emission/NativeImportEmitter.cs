using System;
using System.Linq;
using System.Text;
using BGCS.Core;
using BGCS.Core.Requests;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

internal static class NativeImportEmitter
{
    internal static string Emit(
        CSharpEmissionPlan plan,
        EmissionRequest request
    ) {
        StringBuilder text = new();
        text.Append("public sealed unsafe partial class ").AppendLine(CSharpNames.Identifier(plan.contextName)).AppendLine("{")
            .AppendLine("    private static partial class NativeImports").AppendLine("    {");
        for (int index = 0; index < plan.methods.Length; index++)
        {
            CSharpMethodPlan method = plan.methods[index];
            CSharpPhysicalImportPlan? physical = PhysicalImportPlanner.Find(plan, index);
            if (plan.importMode == BindingImportMode.DllImport)
                text.Append("        [global::System.Runtime.InteropServices.DllImport(").Append(plan.importLibraryName?.reference ?? ContextEmitter.Literal(request.libraryName))
                    .Append(", EntryPoint = ").Append(ContextEmitter.Literal(method.exportName))
                    .Append(", ExactSpelling = true, CallingConvention = global::System.Runtime.InteropServices.CallingConvention.")
                    .Append(method.callingConvention).AppendLine(")]");
            else
            {
                text.Append("        [global::System.Runtime.InteropServices.LibraryImport(").Append(plan.importLibraryName?.reference ?? ContextEmitter.Literal(request.libraryName))
                    .Append(", EntryPoint = ").Append(ContextEmitter.Literal(method.exportName)).AppendLine(")]");
                if (!plan.libraryImportUsesPlatformDefault)
                    text.Append("        [global::System.Runtime.InteropServices.UnmanagedCallConv(CallConvs = new global::System.Type[] { typeof(global::System.Runtime.CompilerServices.CallConv")
                        .Append(method.callingConvention).AppendLine(") })]");
            }
            text.Append("        internal static ").Append(plan.importMode == BindingImportMode.DllImport ? "extern " : "partial ")
                .Append(physical?.returnType ?? method.returnType).Append(' ').Append(physical?.declarationName ?? "Function" + index).Append('(')
                .Append(string.Join(", ", (physical?.parameterTypes ?? method.parameterTypes).Select((
                    type,
                    parameter
                ) => type + " arg" + parameter))).AppendLine(");");
            if (physical is not null) text.Append(PhysicalImportEmitter.Forwarder(plan, physical, table: false));
        }
        return text.AppendLine("    }").AppendLine("}").ToString();
    }
}
