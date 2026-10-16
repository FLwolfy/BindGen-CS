using System;
using System.Linq;
using System.Text;
using BGCS.Contracts;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

internal static class RawApiEmitter
{
    internal static string Emit(CSharpEmissionPlan plan)
    {
        string name = CSharpNames.Identifier(plan.contextName);
        StringBuilder text = new();
        text.Append("public sealed unsafe partial class ").AppendLine(name).AppendLine("{")
            .AppendLine("    public sealed partial class RawApi").AppendLine("    {")
            .Append("        private readonly ").Append(name).AppendLine(" m_owner;")
            .Append("        internal RawApi(").Append(name).AppendLine(" owner) => m_owner = owner;");
        for (int index = 0; index < plan.methods.Length; index++)
        {
            CSharpMethodPlan method = plan.methods[index];
            DocumentationProjector.AppendMethod(text, method.documentation, method.documentationContent, method.parameterNames,
                Enumerable.Range(0, method.parameterNames.Length), method.returnType, raw: true, "        ");
            text.Append("        public ").Append(method.returnType).Append(' ').Append(method.name).Append('(')
                .Append(string.Join(", ", method.parameterTypes.Zip(method.parameterNames, (
                    type,
                    parameter
                ) => type + " " + parameter))).AppendLine(")")
                .AppendLine("        {");
            string local = "__bgcsCall";
            while (method.parameterNames.Any(parameter => parameter.TrimStart('@') == local)) local += "_";
            text.Append("            using var ").Append(local).AppendLine(" = this.m_owner.m_session.AcquireCall();");
            text.Append("            ");
            if (method.returnType != "void") text.Append("return ");
            text.Append(plan.importMode == BindingImportMode.FunctionTable ? "this.m_owner.m_function" + index : "global::" + CSharpNames.Namespace(plan.api.@namespace) + "." + name + ".NativeImports.Function" + index)
                .Append('(').Append(string.Join(", ", method.parameterNames)).AppendLine(");").AppendLine("        }");
        }
        return text.AppendLine("    }").AppendLine("}").ToString();
    }
}
