using System.Text;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

internal static class ManagedEnumEmitter
{
    internal static string Emit(CSharpManagedEnumPlan declaration)
    {
        StringBuilder text = new();
        DocumentationProjector.Append(text, declaration.documentation);
        if (declaration.isFlags) text.AppendLine("[global::System.Flags]");
        text.Append("public enum ").Append(declaration.name).Append(" : ").AppendLine(declaration.underlyingType).AppendLine("{");
        foreach (CSharpEnumValuePlan value in declaration.values)
        {
            DocumentationProjector.Append(text, value.documentation, "    ");
            text.Append("    ").Append(value.name).Append(" = ").Append(value.value).AppendLine(",");
        }
        return text.AppendLine("}").ToString();
    }
}
