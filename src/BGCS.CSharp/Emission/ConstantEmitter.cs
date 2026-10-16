using System.Text;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

internal static class ConstantEmitter
{
    internal static string Emit(CSharpEmissionPlan plan)
    {
        StringBuilder source = new("public sealed unsafe partial class " + plan.contextName + "\n{\n    public static class Constants\n    {\n");
        foreach (CSharpConstantPlan constant in plan.constants)
        {
            DocumentationProjector.Append(source, constant.documentation, "        ");
            source.Append("        ").Append(Member(constant)).AppendLine();
        }
        foreach (CSharpStringConstantPlan constant in plan.stringConstants)
        {
            DocumentationProjector.Append(source, constant.documentation, "        ");
            source.Append("        ").Append(Member(constant)).AppendLine();
        }
        return source.Append("    }\n}\n").ToString();
    }

    internal static string Member(CSharpConstantPlan constant) => "public " + (constant.isCompileTimeConstant ? "const " : "static readonly ")
        + constant.type + " " + constant.name + " = " + constant.expression + ";";
    internal static string Member(CSharpStringConstantPlan constant) => "public " + (constant.isUnicodeString ? "const " : "static ")
        + constant.type + " " + constant.name + (constant.isUnicodeString ? " = " : " => ") + constant.expression + ";";
}
