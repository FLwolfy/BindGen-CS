using System.Linq;
using System.Text;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

internal static class RecordPresentationEmitter
{
    internal static string EmitMembers(CSharpStoragePlan storage)
    {
        if (storage.presentation is not { } presentation) return string.Empty;
        StringBuilder text = new();
        if (presentation.emitSizeConstant)
            text.Append("    public const int NativeSize = ").Append(storage.size).AppendLine(";");
        if (!presentation.constructorFields.IsEmpty)
        {
            text.Append("    public ").Append(storage.name).Append('(').Append(string.Join(", ", presentation.constructorFields.Select(field => field.type + " " + field.name)))
                .AppendLine(")").AppendLine("    {").AppendLine("        this = default;");
            foreach (CSharpFieldPlan field in presentation.constructorFields)
                text.Append("        this.").Append(field.name).Append(" = ").Append(field.name).AppendLine(";");
            text.AppendLine("    }");
        }
        if (!presentation.arrayCopies.IsEmpty)
        {
            text.AppendLine("    /// <summary>Copies exact-length primitive arrays into independent native carrier storage.</summary>")
                .Append("    public ").Append(storage.name).Append('(').Append(string.Join(", ", presentation.constructorFields.Select(field =>
                    (presentation.arrayCopies.SingleOrDefault(copy => copy.nativeField == field.nativeField) is { } array
                        ? "global::System.ReadOnlySpan<" + array.elementType + ">" : field.type) + " " + field.name)))
                .AppendLine(")").AppendLine("    {");
            foreach (CSharpRecordArrayCopyPlan array in presentation.arrayCopies)
                text.Append("        if (").Append(array.fieldName).Append(".Length != ").Append(array.length)
                    .Append(") throw new global::System.ArgumentException(\"The fixed native array requires exactly ").Append(array.length)
                    .Append(" elements.\", nameof(").Append(array.fieldName).AppendLine("));");
            text.AppendLine("        this = default;");
            foreach (CSharpFieldPlan field in presentation.constructorFields)
            {
                CSharpRecordArrayCopyPlan? array = presentation.arrayCopies.SingleOrDefault(copy => copy.nativeField == field.nativeField);
                if (array is null) text.Append("        this.").Append(field.name).Append(" = ").Append(field.name).AppendLine(";");
                else
                {
                    string index = "__bgcsArrayIndex";
                    while (presentation.constructorFields.Any(parameter => parameter.name.TrimStart('@') == index)) index += "_";
                    text.Append("        for (int ").Append(index).Append(" = 0; ").Append(index).Append(" < ").Append(array.length).Append("; ++").Append(index).AppendLine(")")
                        .Append("            this.").Append(field.name).Append('[').Append(index).Append("] = ").Append(field.name).Append('[').Append(index).AppendLine("];");
                }
            }
            text.AppendLine("    }");
        }
        foreach (CSharpRecordArrayCopyOmission omission in presentation.arrayCopyOmissions)
            text.Append("    // Span constructor omitted for ").Append(omission.fieldName).Append(": ").AppendLine(omission.reason);
        if (presentation.validity is { } validity)
        {
            if (validity.constantName is not null)
                text.Append("    private const ").Append(validity.fieldType).Append(' ').Append(validity.constantName).Append(" = ").Append(validity.invalidExpression).AppendLine(";");
            text.Append("    public readonly bool ").Append(validity.propertyName).Append(" => this.").Append(validity.fieldName).Append(" != ")
                .Append(validity.constantName ?? validity.invalidExpression).AppendLine(";");
        }
        return text.ToString();
    }
}
