using System;
using System.Linq;
using System.Text;
using BGCS.Contracts;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

internal static class DirectOwnedFactoryEmitter
{
    internal static string Emit(
        CSharpEmissionPlan plan,
        CSharpDirectOwnedFactoryPlan factory
    ) {
        CSharpMethodPlan method = plan.methods[factory.methodIndex], release = plan.methods[factory.ownership.releaseIndex];
        string runtime = "global::" + CSharpNames.Namespace(plan.runtimeNamespace);
        int[] inputs = Enumerable.Range(0, method.parameterTypes.Length).ToArray();
        int[] visible = OverloadPlanner.PublicInputs(method, inputs).ToArray();
        string[] arguments = method.parameterNames.Select((
            name,
            index
        ) => RecipeConversionEmitter.Input(method, index, name)).ToArray();
        string call = Local("__bgcsCall"), resource = Local("__bgcsResource"), pointer = Local("__bgcsOwnedPointer"), handle = Local("__bgcsOwnedHandle");
        string transferred = Local("__bgcsTransferred"), result = Local("__bgcsOwnedObject");
        string cleanup = Cleanup(factory.methodIndex);
        if (plan.methods.Any(method => method.name.TrimStart('@') == cleanup)) throw new InvalidOperationException("A direct owned cleanup helper conflicts with a configured method.");
        string returnType = factory.ownership.objectName ?? runtime + ".OwnedNativeHandle";
        StringBuilder text = new();
        DocumentationProjector.AppendMethod(text, method.documentation, method.documentationContent, method.parameterNames, visible, returnType, raw: false, "    ");
        text.Append("    public ").Append(returnType).Append(' ').Append(method.name).Append('(')
            .Append(string.Join(", ", visible.Select(index => OverloadPlanner.InputDeclaration(method, index)))).AppendLine(")")
            .AppendLine("    {").Append("        using var ").Append(call).AppendLine(" = this.m_session.AcquireCall();");
        TextInputEmitter.Append(text, method, plan.runtimeNamespace, arguments, inputs);
        bool values = ValueInputEmitter.Open(text, method, arguments, inputs);
        bool buffers = BufferInputEmitter.Open(text, method, arguments, inputs);
        text.Append("        var ").Append(resource).AppendLine(" = this.m_session.AcquireResource();")
            .Append("        ").Append(method.returnType).Append(' ').Append(pointer).AppendLine(" = null;")
            .Append("        ").Append(runtime).Append(".OwnedNativeHandle? ").Append(handle).AppendLine(" = null;")
            .Append("        bool ").Append(transferred).AppendLine(" = false;")
            .AppendLine("        try").AppendLine("        {")
            .Append("            ").Append(pointer).Append(" = ").Append(Invoke(plan, factory.methodIndex)).Append('(').Append(string.Join(", ", arguments)).AppendLine(");")
            .Append("            if (").Append(pointer).AppendLine(" == null) throw new global::System.InvalidOperationException(\"An owned native factory returned null; use Raw to inspect its native failure protocol.\");");
        BufferInputEmitter.Complete(text, method, inputs, "            ");
        text.Append("            ").Append(handle).Append(" = new ").Append(runtime).Append(".OwnedNativeHandle((nint)").Append(pointer).Append(", ").Append(resource)
            .Append(", this.").Append(cleanup).Append(", ").Append(ContextEmitter.Literal(factory.ownership.releaseDomain)).AppendLine(");");
        if (factory.ownership.objectName is not null)
            text.Append("            var ").Append(result).Append(" = new ").Append(factory.ownership.objectName).Append("(this, ").Append(handle).AppendLine(");");
        text.Append("            ").Append(transferred).AppendLine(" = true;")
            .Append("            return ").Append(factory.ownership.objectName is null ? handle : result).AppendLine(";")
            .AppendLine("        }").AppendLine("        finally").AppendLine("        {")
            .Append("            if (!").Append(transferred).AppendLine(")")
            .AppendLine("            {")
            .Append("                if (").Append(handle).Append(" is not null) ").Append(handle).AppendLine(".Dispose();")
            .AppendLine("                else").AppendLine("                {")
            .AppendLine("                    try").AppendLine("                    {")
            .Append("                        if (").Append(pointer).Append(" != null) this.").Append(cleanup).Append("((nint)").Append(pointer).AppendLine(");")
            .AppendLine("                    }")
            .Append("                    finally { ").Append(resource).AppendLine(".Dispose(); }")
            .AppendLine("                }").AppendLine("            }").AppendLine("        }");
        BufferInputEmitter.Close(text, buffers);
        ValueInputEmitter.Close(text, values);
        text.AppendLine("    }").Append("    private void ").Append(cleanup).AppendLine("(nint address)")
            .Append("        => ").Append(Invoke(plan, factory.ownership.releaseIndex)).Append("((").Append(release.parameterTypes[0]).AppendLine(")address);");
        return text.ToString();

        string Local(string name)
        {
            while (method.parameterNames.Any(parameter => parameter.TrimStart('@') == name)) name += "_";
            return name;
        }
    }

    internal static string Cleanup(int methodIndex) => "__BgcsReleaseDirect" + methodIndex;
    internal static string Invoke(
        CSharpEmissionPlan plan,
        int index
    ) => plan.importMode == BindingImportMode.FunctionTable ? "this.m_function" + index
        : "global::" + CSharpNames.Namespace(plan.api.@namespace) + "." + plan.contextName + ".NativeImports.Function" + index;
}
