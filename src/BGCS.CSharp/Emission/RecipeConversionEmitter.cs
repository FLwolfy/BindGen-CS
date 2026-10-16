using System;
using System.Linq;
using System.Text;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

internal static class RecipeConversionEmitter
{
    internal static string Emit(CSharpEmissionPlan plan)
    {
        if (plan.contextName.TrimStart('@') == "RecipeConversions") throw new InvalidOperationException("The API context name conflicts with its configured recipe helper container.");
        StringBuilder text = new();
        text.Append("public sealed unsafe partial class ").AppendLine(CSharpNames.Identifier(plan.contextName)).AppendLine("{")
            .AppendLine("    public static class RecipeConversions").AppendLine("    {");
        foreach (CSharpExternalTypeProjectionPlan projection in plan.externalTypeProjections)
        {
            text.Append("        private static ").Append(projection.rawCarrierType).Append(" __ToExternalNative_").Append(projection.id)
                .Append('(').Append(projection.managedType).AppendLine(" value)").AppendLine("        {")
                .Append("            ").Append(projection.rawCarrierType).AppendLine(" result = default;");
            foreach (CSharpExternalFieldCopyPlan field in projection.fields)
                AppendField(field.rawField, field.managedField, field.isNativeAddress);
            text.AppendLine("            return result;").AppendLine("        }");
            text.Append("        private static ").Append(projection.managedType).Append(" __FromExternalNative_").Append(projection.id)
                .Append('(').Append(projection.rawCarrierType).AppendLine(" value)").AppendLine("        {")
                .Append("            ").Append(projection.managedType).AppendLine(" result = default;");
            foreach (CSharpExternalFieldCopyPlan field in projection.fields)
                AppendField(field.managedField, field.rawField, field.isNativeAddress);
            text.AppendLine("            return result;").AppendLine("        }");
        }
        foreach (CSharpRecipeProjectionPlan projection in plan.recipeProjections)
        {
            if (projection.toNativeExpression is not null)
                text.Append("        internal static ").Append(projection.carrierType).Append(' ').Append(ToNative(projection)).Append('(').Append(projection.managedType)
                    .Append(" value) => ").Append(projection.toNativeExpression).AppendLine(";");
            if (projection.fromNativeExpression is not null)
                text.Append("        internal static ").Append(projection.managedType).Append(' ').Append(FromNative(projection)).Append('(').Append(projection.carrierType)
                    .Append(" value) => ").Append(projection.fromNativeExpression).AppendLine(";");
        }
        foreach (CSharpRecipeAliasPlan alias in plan.recipeAliases)
        {
            CSharpRecipeProjectionPlan projection = plan.recipeProjections.Single(projection => projection.id == alias.projectionId);
            if (projection.toNativeExpression is not null)
                text.Append("        public static ").Append(projection.carrierType).Append(" ToNative_").Append(alias.name).Append('(').Append(projection.managedType)
                    .Append(" value) => ").Append(ToNative(projection)).AppendLine("(value);");
            if (projection.fromNativeExpression is not null)
                text.Append("        public static ").Append(projection.managedType).Append(" FromNative_").Append(alias.name).Append('(').Append(projection.carrierType)
                    .Append(" value) => ").Append(FromNative(projection)).AppendLine("(value);");
        }
        return text.AppendLine("    }").AppendLine("}").ToString();

        void AppendField(
            string target,
            string source,
            bool address
        ) {
            if (address)
                text.Append("            global::System.Runtime.CompilerServices.Unsafe.WriteUnaligned<nint>((void*)(&result.").Append(target)
                    .Append("), global::System.Runtime.CompilerServices.Unsafe.ReadUnaligned<nint>((void*)(&value.").Append(source).AppendLine(")));");
            else text.Append("            result.").Append(target).Append(" = value.").Append(source).AppendLine(";");
        }
    }

    internal static string InputType(
        CSharpMethodPlan method,
        int index
    ) => OverloadPlanner.InputType(method, index);
    internal static string Input(
        CSharpMethodPlan method,
        int index,
        string expression
    ) => method.valueProjections.TryGetValue(index, out CSharpRecipeProjectionPlan? projection) && projection.toNativeExpression is not null
        ? "RecipeConversions." + ToNative(projection) + "(" + expression + ")" : expression;
    internal static string ResultType(
        CSharpMethodPlan method,
        CSharpStatusMethodPlan status
    ) => method.valueProjections.TryGetValue(status.resultIndex, out CSharpRecipeProjectionPlan? projection) && projection.fromNativeExpression is not null
        ? projection.managedType : status.resultType;
    internal static string Result(
        CSharpMethodPlan method,
        int index,
        string expression
    ) => method.valueProjections.TryGetValue(index, out CSharpRecipeProjectionPlan? projection) && projection.fromNativeExpression is not null
        ? "RecipeConversions." + FromNative(projection) + "(" + expression + ")" : expression;
    internal static string DirectResultType(CSharpMethodPlan method) => method.returnProjection is { fromNativeExpression: not null } projection ? projection.managedType : method.returnType;
    internal static string DirectResult(
        CSharpMethodPlan method,
        string expression
    ) => method.returnProjection is { fromNativeExpression: not null } projection
        ? "RecipeConversions." + FromNative(projection) + "(" + expression + ")" : expression;
    private static string ToNative(CSharpRecipeProjectionPlan projection) => "__ToNative_" + projection.id;
    private static string FromNative(CSharpRecipeProjectionPlan projection) => "__FromNative_" + projection.id;
}
