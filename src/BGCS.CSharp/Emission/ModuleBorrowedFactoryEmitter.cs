using System;
using System.Linq;
using System.Text;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

internal static class ModuleBorrowedFactoryEmitter
{
    internal static string Emit(
        CSharpEmissionPlan plan,
        CSharpModuleBorrowedFactoryPlan factory
    ) {
        CSharpMethodPlan method = plan.methods[factory.methodIndex];
        string runtime = "global::" + CSharpNames.Namespace(plan.runtimeNamespace);
        string returnType = factory.objectName ?? runtime + ".ModuleBorrowedNativeHandle";
        int[] inputs = Enumerable.Range(0, method.parameterTypes.Length).ToArray();
        int[] visible = OverloadPlanner.PublicInputs(method, inputs).ToArray();
        string[] arguments = method.parameterNames.Select((
            name,
            index
        ) => RecipeConversionEmitter.Input(method, index, name)).ToArray();
        string call = Local("__bgcsCall"), resource = Local("__bgcsResource"), pointer = Local("__bgcsBorrowedPointer");
        string handle = Local("__bgcsBorrowedHandle"), transferred = Local("__bgcsTransferred"), result = Local("__bgcsBorrowedObject");
        StringBuilder text = new();
        DocumentationProjector.AppendMethod(text, method.documentation, method.documentationContent, method.parameterNames,
            visible, returnType, raw: false, "    ");
        text.Append("    public ").Append(returnType).Append(' ').Append(method.name).Append('(')
            .Append(string.Join(", ", visible.Select(index => OverloadPlanner.InputDeclaration(method, index)))).AppendLine(")")
            .AppendLine("    {").Append("        using var ").Append(call).AppendLine(" = this.m_session.AcquireCall();");
        TextInputEmitter.Append(text, method, plan.runtimeNamespace, arguments, inputs);
        bool values = ValueInputEmitter.Open(text, method, arguments, inputs);
        bool buffers = BufferInputEmitter.Open(text, method, arguments, inputs);
        text.Append("        var ").Append(resource).AppendLine(" = this.m_session.AcquireResource();")
            .Append("        ").Append(runtime).Append(".ModuleBorrowedNativeHandle? ").Append(handle).AppendLine(" = null;")
            .Append("        bool ").Append(transferred).AppendLine(" = false;")
            .AppendLine("        try").AppendLine("        {")
            .Append("            ").Append(method.returnType).Append(' ').Append(pointer).Append(" = ")
            .Append(DirectOwnedFactoryEmitter.Invoke(plan, factory.methodIndex)).Append('(').Append(string.Join(", ", arguments)).AppendLine(");")
            .Append("            if (").Append(pointer).AppendLine(" == null) throw new global::System.InvalidOperationException(\"A module-borrowed getter returned null; use Raw to inspect its native failure protocol.\");");
        BufferInputEmitter.Complete(text, method, inputs, "            ");
        text.Append("            ").Append(handle).Append(" = new ").Append(runtime).Append(".ModuleBorrowedNativeHandle((nint)")
            .Append(pointer).Append(", ").Append(resource).AppendLine(");");
        if (factory.objectName is not null)
            text.Append("            var ").Append(result).Append(" = new ").Append(factory.objectName).Append("(this, ").Append(handle).AppendLine(");");
        text.Append("            ").Append(transferred).AppendLine(" = true;")
            .Append("            return ").Append(factory.objectName is null ? handle : result).AppendLine(";")
            .AppendLine("        }").AppendLine("        finally").AppendLine("        {")
            .Append("            if (!").Append(transferred).AppendLine(")")
            .AppendLine("            {").Append("                if (").Append(handle).Append(" is not null) ").Append(handle).AppendLine(".Dispose();")
            .Append("                else ").Append(resource).AppendLine(".Dispose();")
            .AppendLine("            }").AppendLine("        }");
        BufferInputEmitter.Close(text, buffers);
        ValueInputEmitter.Close(text, values);
        return text.AppendLine("    }").ToString();

        string Local(string name)
        {
            while (method.parameterNames.Any(parameter => parameter.TrimStart('@') == name)) name += "_";
            return name;
        }
    }
}
