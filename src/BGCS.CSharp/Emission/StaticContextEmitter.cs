using System;
using System.Text;
using BGCS.Contracts.Requests;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

internal static class StaticContextEmitter
{
    internal static string Emit(
        CSharpEmissionPlan plan,
        EmissionRequest request
    )
        => Emit(plan, request.libraryName);

    internal static string Emit(
        CSharpEmissionPlan plan,
        string libraryName
    ) {
        StaticExportTableDescriptor table = plan.staticExportTable ?? throw new InvalidOperationException("A static adapter contract is required.");
        string runtime = "global::" + CSharpNames.Namespace(plan.runtimeNamespace);
        StringBuilder text = new();
        bool allocator = ContextEmitter.HasTemporaryAllocator(plan);
        text.Append("    public static ").Append(CSharpNames.Identifier(plan.contextName)).Append(" FromStatic(")
            .Append(allocator ? ContextEmitter.TemporaryAllocatorParameter(plan.runtimeNamespace) : string.Empty).AppendLine(")")
            .AppendLine("    {")
            .Append("        if (StaticNativeAdapter.GetRevision() != 1u || StaticNativeAdapter.GetCount() != ").Append(table.exportNames.Length).AppendLine("u")
            .Append("            || StaticNativeAdapter.GetPointerSize() != ").Append(plan.api.interop.pointerSize).AppendLine("u")
            .Append("            || global::System.IntPtr.Size != ").Append(plan.api.interop.pointerSize).AppendLine(")")
            .AppendLine("            throw new global::System.BadImageFormatException(\"The static native table revision, extent or pointer width differs from the binding contract.\");")
            .AppendLine("        byte* identity = StaticNativeAdapter.GetAbiIdentity();")
            .Append("        const string expected = ").Append(ContextEmitter.Literal(table.abiIdentity)).AppendLine(";")
            .AppendLine("        if (identity == null) throw new global::System.BadImageFormatException(\"The static native table has no ABI identity.\");")
            .AppendLine("        for (int index = 0; index < expected.Length; index++)")
            .AppendLine("            if (identity[index] != expected[index]) throw new global::System.BadImageFormatException(\"The static native table ABI identity differs from the binding contract.\");")
            .AppendLine("        if (identity[expected.Length] != 0) throw new global::System.BadImageFormatException(\"The static native table ABI identity has an invalid extent.\");")
            .Append("        var symbols = new global::System.Collections.Generic.KeyValuePair<string, nint>[").Append(table.exportNames.Length).AppendLine("];");
        for (int index = 0; index < table.exportNames.Length; index++)
            text.Append("        symbols[").Append(index).Append("] = new(").Append(ContextEmitter.Literal(table.exportNames[index]))
                .Append(", unchecked((nint)StaticNativeAdapter.GetExport(").Append(index).AppendLine("u)));");
        text.Append("        return FromProvider(new ").Append(runtime).Append(".StaticModuleProvider(symbols)")
            .Append(allocator ? ", temporaryAllocatorFactory" : string.Empty).AppendLine(");")
            .AppendLine("    }").AppendLine("    private static class StaticNativeAdapter").AppendLine("    {");
        Import("GetExport", table.getExportSymbol, "nuint", "uint index");
        Import("GetRevision", table.getRevisionSymbol, "uint", string.Empty);
        Import("GetCount", table.getCountSymbol, "uint", string.Empty);
        Import("GetPointerSize", table.getPointerSizeSymbol, "uint", string.Empty);
        Import("GetAbiIdentity", table.getAbiIdentitySymbol, "byte*", string.Empty);
        return text.AppendLine("    }").ToString();

        void Import(
            string name,
            string export,
            string result,
            string parameters
        ) {
            text.Append("        [global::System.Runtime.InteropServices.DllImport(").Append(ContextEmitter.Literal(libraryName))
                .Append(", EntryPoint = ").Append(ContextEmitter.Literal(export))
                .AppendLine(", ExactSpelling = true, CallingConvention = global::System.Runtime.InteropServices.CallingConvention.Cdecl)]")
                .Append("        internal static extern ").Append(result).Append(' ').Append(name).Append('(').Append(parameters).AppendLine(");");
        }
    }
}
