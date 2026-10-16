using System;
using System.Linq;
using System.Text;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

internal static class PointerCarrierEmitter
{
    internal static string Emit(
        CSharpEmissionPlan plan,
        CSharpPointerCarrierPlan carrier
    ) {
        string name = carrier.name;
        StringBuilder text = new();
        DocumentationProjector.Append(text, carrier.documentation ?? "A non-owning native address.");
        text.AppendLine("/// <remarks>Every Dangerous view requires a live allocation, correct extent and caller-controlled synchronization. This address does not own or retain native memory.</remarks>")
            .Append("public readonly unsafe partial struct ").Append(name).Append(" : global::System.IEquatable<").Append(name).AppendLine(">")
            .AppendLine("{").AppendLine("    private readonly nint m_handle;")
            .Append("    public ").Append(name).AppendLine("(nint address) { this.m_handle = address; }")
            .AppendLine("    public nint Handle => this.m_handle;")
            .AppendLine("    public bool IsNull => this.m_handle == 0;")
            .Append("    public static ").Append(name).AppendLine(" Null => default;")
            .Append("    public bool Equals(").Append(name).AppendLine(" other) => this.m_handle == other.m_handle;")
            .Append("    public override bool Equals(object? other) => other is ").Append(name).AppendLine(" address && this.Equals(address);")
            .AppendLine("    public override int GetHashCode() => this.m_handle.GetHashCode();")
            .Append("    public static bool operator ==(").Append(name).Append(" left, ").Append(name).AppendLine(" right) => left.Equals(right);")
            .Append("    public static bool operator !=(").Append(name).Append(" left, ").Append(name).AppendLine(" right) => !left.Equals(right);");
        if (carrier.exposesTypedPointer)
            text.Append("    public ").Append(carrier.pointerType).Append(" DangerousGetPointer() => (").Append(carrier.pointerType).AppendLine(")this.CheckAddress();");
        if (carrier.elementSize is int size)
        {
            string modifier = carrier.elementReadOnly ? "ref readonly " : "ref ";
            text.Append("    public ").Append(modifier).Append(carrier.elementType).Append(" DangerousGetReference() => ref *(").Append(carrier.elementType).AppendLine("*)this.CheckAddress();")
                .Append("    public ").Append(modifier).Append(carrier.elementType).AppendLine(" DangerousGetElement(nint index)")
                .AppendLine("    {")
                .Append("        nint address = checked((nint)this.CheckAddress() + checked(index * ").Append(size).AppendLine("));")
                .Append("        return ref *(").Append(carrier.elementType).AppendLine("*)address;")
                .AppendLine("    }");
        }
        if (carrier.elementCarrier is string lower)
        {
            string type = CSharpGeneratedTypeScope.QualifiedReference(plan.api, lower);
            string slot = "*(" + carrier.elementType + "*)checked((nint)this.CheckAddress() + checked(index * " + carrier.pointerSize + "))";
            text.Append("    public ").Append(type).Append(" DangerousGetAddressElement(nint index) => new ").Append(type).Append("((nint)(").Append(slot).AppendLine("));");
            if (!carrier.readOnly) text.Append("    public void DangerousSetAddressElement(nint index, ").Append(type).Append(" value) => ").Append(slot)
                .Append(" = (").Append(carrier.elementType).AppendLine(")value.Handle;");
        }
        foreach (CSharpPointerFieldPlan field in carrier.fields)
        {
            string suffix = field.name.TrimStart('@');
            string address = "(this.CheckAddress() + " + field.offset + ")";
            if (field.pointer)
            {
                string publicType = field.carrier is null ? "nint" : CSharpGeneratedTypeScope.QualifiedReference(plan.api, field.carrier);
                string value = "*(" + field.type + "*)" + address;
                text.Append("    public ").Append(publicType).Append(" DangerousField_").Append(suffix).AppendLine()
                    .AppendLine("    {").Append("        get => ");
                if (field.carrier is null) text.Append("(nint)(").Append(value).Append(')');
                else text.Append("new ").Append(publicType).Append("((nint)(").Append(value).Append("))");
                text.AppendLine(";");
                if (!field.readOnly) text.Append("        set => ").Append(value).Append(" = (").Append(field.type).Append(")value").Append(field.carrier is null ? string.Empty : ".Handle").AppendLine(";");
                text.AppendLine("    }");
            }
            else if (field.arrayLength is int length)
            {
                string span = "global::System." + (field.readOnly ? "ReadOnlySpan<" : "Span<") + field.type + ">";
                text.Append("    public ").Append(span).Append(" DangerousArray_").Append(suffix).Append("() => new ").Append(span)
                    .Append('(').Append(address).Append(", ").Append(length).AppendLine(");");
            }
            else if (field.bitfield)
            {
                string access = "((" + carrier.elementType + "*)this.CheckAddress())->" + field.name;
                text.Append("    public ").Append(field.type).Append(" DangerousField_").Append(suffix).AppendLine()
                    .AppendLine("    {").Append("        get => ").Append(access).AppendLine(";");
                if (!field.readOnly) text.Append("        set => ").Append(access).AppendLine(" = value;");
                text.AppendLine("    }");
            }
            else text.Append("    public ref ").Append(field.readOnly ? "readonly " : string.Empty).Append(field.type).Append(" DangerousField_")
                .Append(suffix).Append("() => ref *(").Append(field.type).Append("*)").Append(address).AppendLine(";");
        }
        foreach (CSharpPointerFunctionPlan operation in plan.pointerFunctions.Where(operation => operation.receiverCarrier == carrier.name))
            text.Append(PointerReceiverEmitter.Emit(plan, operation));
        text.AppendLine("    private byte* CheckAddress()")
            .AppendLine("    {")
            .Append("        if (global::System.IntPtr.Size != ").Append(carrier.pointerSize).AppendLine(") throw new global::System.PlatformNotSupportedException(\"The native address target has a different pointer width.\");")
            .AppendLine("        if (this.m_handle == 0) throw new global::System.InvalidOperationException(\"A null native address cannot be dereferenced.\");");
        if (carrier.elementAlignment is int alignment)
            text.Append("        if ((nuint)this.m_handle % ").Append(alignment).AppendLine(" != 0) throw new global::System.InvalidOperationException(\"The native address does not satisfy its required element alignment.\");");
        return text.AppendLine("        return (byte*)this.m_handle;").AppendLine("    }").AppendLine("}").ToString();
    }
}
