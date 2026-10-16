using System.Text;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

internal static class BitfieldEmitter
{
    internal static string Emit(CSharpBitfieldPlan field)
    {
        StringBuilder text = new();
        DocumentationProjector.Append(text, field.documentation, "    ");
        text.Append("    public ").Append(field.type).Append(' ').AppendLine(field.name).AppendLine("    {")
            .AppendLine("        get").AppendLine("        {").AppendLine("            ulong bits = 0;")
            .AppendLine("            fixed (byte* storage = m_bitStorage)").AppendLine("            {")
            .Append("                for (int index = 0; index < ").Append(field.width).AppendLine("; index++)")
            .AppendLine("                {").Append("                    long position = ").Append(field.bitOffset).AppendLine("L + index;")
            .AppendLine("                    bits |= (ulong)((storage[position >> 3] >> (int)(position & 7)) & 1) << index;")
            .AppendLine("                }").AppendLine("            }");
        if (field.type == "bool") text.AppendLine("            return bits != 0;");
        else
        {
            text.Append("            return unchecked((").Append(field.type).Append(")");
            if (field.isSigned && field.width < 64)
                text.Append("((long)(bits << ").Append(64 - field.width).Append(") >> ").Append(64 - field.width).Append(')');
            else text.Append(field.isSigned ? "(long)bits" : "bits");
            text.AppendLine(");");
        }
        text.AppendLine("        }").AppendLine("        set").AppendLine("        {")
            .AppendLine(field.type == "bool" ? "            ulong bits = value ? 1UL : 0UL;" : "            ulong bits = unchecked((ulong)value);")
            .AppendLine("            fixed (byte* storage = m_bitStorage)").AppendLine("            {")
            .Append("                for (int index = 0; index < ").Append(field.width).AppendLine("; index++)")
            .AppendLine("                {").Append("                    long position = ").Append(field.bitOffset).AppendLine("L + index;")
            .AppendLine("                    int shift = (int)(position & 7);")
            .AppendLine("                    storage[position >> 3] = (byte)((storage[position >> 3] & ~(1 << shift)) | (int)((bits >> index) & 1) << shift);")
            .AppendLine("                }").AppendLine("            }").AppendLine("        }").AppendLine("    }");
        return text.ToString();
    }
}
