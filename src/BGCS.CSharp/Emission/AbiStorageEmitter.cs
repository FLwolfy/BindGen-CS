using System;
using System.Text;
using BGCS.CSharp.Planning;
using BGCS.Contracts.Native;

namespace BGCS.CSharp.Emission;

internal static class AbiStorageEmitter
{
    internal static string Emit(CSharpStoragePlan storage)
    {
        StringBuilder text = new();
        DocumentationProjector.Append(text, storage.documentation);
        if (storage.enumUnderlyingType is string integral)
        {
            text.Append("public enum ").Append(storage.name).Append(" : ").AppendLine(integral).AppendLine("{");
            foreach (CSharpEnumValuePlan value in storage.enumValues)
            {
                DocumentationProjector.Append(text, value.documentation, "    ");
                text.Append("    ").Append(value.name).Append(" = ").Append(value.value).AppendLine(",");
            }
            return text.AppendLine("}").ToString();
        }
        if (storage.arrayLength is int length)
        {
            if (storage.pointerArrayWidth is int pointerWidth)
            {
                string slot = pointerWidth == 4 ? "uint" : "ulong";
                text.Append("[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Sequential, Size = ").Append(storage.size).AppendLine(")]")
                    .Append("public unsafe partial struct ").AppendLine(storage.name).AppendLine("{")
                    .Append("    private fixed ").Append(slot).Append(" m_elements[").Append(length).AppendLine("];")
                    .Append("    public ").Append(storage.arrayElementType).AppendLine(" this[int index]").AppendLine("    {");
                foreach (string accessor in new[] { "get", "set" })
                {
                    text.Append("        ").AppendLine(accessor).AppendLine("        {")
                        .Append("            if ((uint)index >= ").Append(length).AppendLine("U) throw new global::System.ArgumentOutOfRangeException(nameof(index));")
                        .Append("            if (sizeof(nint) != ").Append(pointerWidth).AppendLine(") throw new global::System.PlatformNotSupportedException(\"The native pointer array width differs from this process.\");")
                        .Append("            fixed (").Append(slot).AppendLine("* storage = m_elements)");
                    if (accessor == "get") text.Append("                return (").Append(storage.arrayElementType).AppendLine(")(nuint)storage[index];");
                    else text.Append("                storage[index] = (").Append(slot).AppendLine(")(nuint)value;");
                    text.AppendLine("        }");
                }
                return text.AppendLine("    }").AppendLine("}").ToString();
            }
            text.Append("[global::System.Runtime.CompilerServices.InlineArray(").Append(length).AppendLine(")]");
            text.Append("public partial struct ").AppendLine(storage.name).AppendLine("{");
            return text.Append("    private ").Append(storage.arrayElementType).AppendLine(" _element0;").AppendLine("}").ToString();
        }
        if (storage.size is int size)
        {
            text.Append("// Native allocation alignment: ").Append(storage.alignment).AppendLine(" bytes. Use aligned native memory when required.");
            text.Append("[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Explicit, Pack = ").Append(storage.packing).Append(", Size = ")
                .Append(size).AppendLine(")]");
        }
        else text.AppendLine("// Opaque native marker: storage size and by-value use are intentionally unavailable.");
        text.Append("public unsafe partial struct ").AppendLine(storage.name).AppendLine("{");
        if (!storage.bitfields.IsEmpty)
            text.AppendLine("    [global::System.Runtime.InteropServices.FieldOffset(0)]")
                .Append("    private fixed byte m_bitStorage[").Append(storage.size).AppendLine("];");
        foreach (CSharpFieldPlan field in storage.fields)
        {
            DocumentationProjector.Append(text, field.documentation, "    ");
            text.Append("    [global::System.Runtime.InteropServices.FieldOffset(").Append(field.offset).AppendLine(")]");
            text.Append("    public ").Append(field.type).Append(' ').Append(field.name).AppendLine(";");
        }
        foreach (CSharpFlexibleArrayPlan tail in storage.flexibleArrays)
        {
            DocumentationProjector.Append(text, tail.documentation, "    ");
            text.AppendLine("    // The caller owns native allocation, extent and lifetime; this accessor supplies no managed span.");
            text.Append("    public static ").Append(tail.elementType).Append("* ").Append(tail.name).Append('(').Append(storage.name).AppendLine("* instance)")
                .AppendLine("    {").AppendLine("        if (instance == null) throw new global::System.ArgumentNullException(nameof(instance));")
                .Append("        return (").Append(tail.elementType).Append("*)((byte*)instance + ").Append(tail.offset).AppendLine(");").AppendLine("    }");
        }
        foreach (CSharpBitfieldPlan field in storage.bitfields) text.Append(BitfieldEmitter.Emit(field));
        text.Append(RecordPresentationEmitter.EmitMembers(storage));
        foreach (CSharpValueReceiverPlan member in storage.valueMembers) text.Append(ValueReceiverEmitter.Emit(member));
        return text.AppendLine("}").ToString();
    }

    internal static string EmitGuards(CSharpEmissionPlan plan)
    {
        StringBuilder text = new();
        text.Append("        if (global::System.IntPtr.Size != ").Append(plan.api.interop.pointerSize).AppendLine(")");
        text.AppendLine("            throw new global::System.PlatformNotSupportedException(\"The native target pointer width differs from this process.\");");
        if (plan.api.interop.byteOrder != NativeByteOrder.Unknown)
        {
            text.Append("        if (global::System.BitConverter.IsLittleEndian != ")
                .Append(plan.api.interop.byteOrder == NativeByteOrder.LittleEndian ? "true" : "false").AppendLine(")");
            text.AppendLine("            throw new global::System.PlatformNotSupportedException(\"The native target byte order differs from this process.\");");
        }
        foreach (CSharpStoragePlan storage in plan.storage)
        {
            if (storage.size is not int size) continue;
            string type = CSharpGeneratedTypeScope.DeclarationReference(plan.api, storage.name);
            text.Append("        if (sizeof(").Append(type).Append(") != ").Append(size).AppendLine(")");
            text.AppendLine("            throw new global::System.TypeLoadException(\"A managed storage carrier differs from the recorded native size.\");");
            foreach (CSharpFieldPlan field in storage.fields)
            {
                text.Append("        if (global::System.Runtime.InteropServices.Marshal.OffsetOf<").Append(type).Append(">(")
                    .Append(Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(field.name.TrimStart('@'), true)).Append(").ToInt64() != ")
                    .Append(field.offset).AppendLine(")");
                text.AppendLine("            throw new global::System.TypeLoadException(\"A managed field offset differs from the recorded native offset.\");");
            }
            if (storage.managedAlignment is int alignment)
            {
                text.Append("        if (global::System.Runtime.InteropServices.Marshal.OffsetOf<").Append(CSharpGeneratedTypeScope.DeclarationReference(plan.api, storage.alignmentProbeName!)).Append(">(")
                    .Append("\"value\").ToInt64() != ").Append(alignment).AppendLine(")");
                text.AppendLine("            throw new global::System.TypeLoadException(\"A managed aggregate alignment differs from the recorded native alignment.\");");
            }
        }
        return text.ToString();
    }

    internal static string EmitAlignmentProbe(CSharpStoragePlan storage)
    {
        if (storage.managedAlignment is null) return string.Empty;
        return "[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Sequential)]\n"
            + "internal unsafe struct " + storage.alignmentProbeName + "\n{\n    public byte prefix;\n    public " + storage.name + " value;\n}\n";
    }
}
