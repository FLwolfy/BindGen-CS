using System;
using System.Linq;
using System.Text;
using BGCS.Core;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

internal static class SharedContextEmitter
{
    internal static string Emit(
        CSharpEmissionPlan plan,
        string libraryName
    ) {
        CSharpSharedContextPlan shared = plan.sharedContext ?? throw new InvalidOperationException("An explicit shared lifecycle plan is required.");
        string context = SharedContextPlanner.ContextType(plan);
        bool allocator = ContextEmitter.HasTemporaryAllocator(plan);
        bool dispatcher = ContextEmitter.HasCallbackDispatcher(plan);
        string optional = string.Join(", ", new[] { dispatcher ? "global::" + shared.runtimeNamespace + ".NativeCallbackDispatcher callbackDispatcher" : null,
            allocator ? ContextEmitter.TemporaryAllocatorParameter(shared.runtimeNamespace) : null }.OfType<string>());
        string forwarded = string.Join(", ", new[] { dispatcher ? "callbackDispatcher" : null, allocator ? "temporaryAllocatorFactory" : null }.OfType<string>());
        StringBuilder text = new();
        text.AppendLine("/// <summary>Owns one explicitly shared context. Replacement closes the previous context; existing resource leases retain its native cleanup capability.</summary>")
            .Append("public static unsafe partial class ").AppendLine(shared.name).AppendLine("{")
            .Append(LibraryNameEmitter.Emit(plan, libraryName))
            .Append("    private static ").Append(context).AppendLine("? m_context;")
            .Append("    private static ").Append(context).AppendLine(" Current => global::System.Threading.Volatile.Read(ref m_context)")
            .AppendLine("        ?? throw new global::System.InvalidOperationException(\"The shared binding context must be initialized before use.\");")
            .AppendLine("    public static bool IsInitialized => global::System.Threading.Volatile.Read(ref m_context) is not null;")
            .Append("    public static ").Append(context).AppendLine(".RawApi Raw => Current.Raw;")
            .Append("    private static void Install(").Append(context).AppendLine(" candidate)")
            .AppendLine("    {")
            .AppendLine("        var previous = global::System.Threading.Interlocked.Exchange(ref m_context, candidate);")
            .AppendLine("        previous?.Dispose();")
            .AppendLine("    }");
        foreach (CSharpFunctionContainerPlan container in plan.functionContainers)
            text.Append("    public static ").Append(container.viewType).Append(' ').Append(container.name)
                .Append(" => Current.").Append(container.name).AppendLine(";");
        if (plan.importMode == BindingImportMode.FunctionTable)
        {
            text.Append("    public static void FromProvider(global::").Append(shared.runtimeNamespace).Append(".INativeModuleProvider provider")
                .Append(optional.Length != 0 ? ", " + optional : string.Empty).Append(") => Install(")
                .Append(context).Append(".FromProvider(provider").Append(forwarded.Length != 0 ? ", " + forwarded : string.Empty).AppendLine("));");
            if (shared.canLoad)
                text.Append("    public static void Load(").Append(dispatcher ? "global::" + shared.runtimeNamespace + ".NativeCallbackDispatcher callbackDispatcher, " : string.Empty)
                    .Append("string libraryPath = ").Append(ContextEmitter.Literal(libraryName))
                    .Append(allocator ? ", " + ContextEmitter.TemporaryAllocatorParameter(shared.runtimeNamespace) : string.Empty).Append(") => Install(")
                    .Append(context).Append(".Load(").Append(dispatcher ? "callbackDispatcher, " : string.Empty).Append("libraryPath")
                    .Append(allocator ? ", temporaryAllocatorFactory" : string.Empty).AppendLine("));");
            if (shared.canFromStatic) text.Append("    public static void FromStatic(").Append(optional).Append(") => Install(")
                .Append(context).Append(".FromStatic(").Append(forwarded).AppendLine("));");
        }
        else text.Append("    public static void Create(").Append(optional).Append(") => Install(")
            .Append(context).Append(".Create(").Append(forwarded).AppendLine("));");
        text.AppendLine("    public static void Dispose() => global::System.Threading.Interlocked.Exchange(ref m_context, null)?.Dispose();")
            .AppendLine("    public static global::System.Threading.Tasks.ValueTask DisposeAsync()")
            .AppendLine("    {")
            .AppendLine("        var previous = global::System.Threading.Interlocked.Exchange(ref m_context, null);")
            .AppendLine("        return previous is null ? default : previous.DisposeAsync();")
            .AppendLine("    }");
        foreach (CSharpFacadeMethodPlan method in shared.methods)
        {
            DocumentationProjector.AppendMethod(text, method.documentation, method.documentationContent, method.parameterNames,
                Enumerable.Range(0, method.parameterNames.Length), method.returnType, raw: false, "    ");
            text.Append("    public static ").Append(method.returnType).Append(' ').Append(method.name).Append('(')
                .Append(string.Join(", ", Enumerable.Range(0, method.parameterTypes.Length).Select(index => FacadeParameters.Declaration(method, index))))
                .Append(") => Current.").Append(method.name).Append('(')
                .Append(string.Join(", ", Enumerable.Range(0, method.parameterNames.Length).Select(index => FacadeParameters.Argument(method, index)))).AppendLine(");");
        }
        foreach (CSharpVirtualProxyPlan proxy in plan.virtualProxies)
            text.Append("    public static TResult With").Append(proxy.name).Append("Callbacks<TResult>(").Append(context).Append('.').Append(proxy.interfaceName)
                .Append(" target, ").Append(context).Append('.').Append(proxy.name).Append("NativeCall<TResult> operation) => Current.With")
                .Append(proxy.name).AppendLine("Callbacks(target, operation);");
        if (!plan.constants.IsEmpty || !plan.stringConstants.IsEmpty)
        {
            text.AppendLine("    public static class Constants").AppendLine("    {");
            foreach (CSharpConstantPlan constant in plan.constants)
            {
                DocumentationProjector.Append(text, constant.documentation, "        ");
                text.Append("        ").AppendLine(ConstantEmitter.Member(constant));
            }
            foreach (CSharpStringConstantPlan constant in plan.stringConstants)
            {
                DocumentationProjector.Append(text, constant.documentation, "        ");
                text.Append("        ").AppendLine(ConstantEmitter.Member(constant));
            }
            text.AppendLine("    }");
        }
        if (!plan.recipeAliases.IsEmpty)
        {
            text.AppendLine("    public static class RecipeConversions").AppendLine("    {");
            foreach (CSharpRecipeAliasPlan alias in plan.recipeAliases)
            {
                CSharpRecipeProjectionPlan projection = plan.recipeProjections.Single(projection => projection.id == alias.projectionId);
                if (projection.toNativeExpression is not null)
                    text.Append("        public static ").Append(projection.carrierType).Append(" ToNative_").Append(alias.name).Append('(').Append(projection.managedType)
                        .Append(" value) => ").Append(context).Append(".RecipeConversions.ToNative_").Append(alias.name).AppendLine("(value);");
                if (projection.fromNativeExpression is not null)
                    text.Append("        public static ").Append(projection.managedType).Append(" FromNative_").Append(alias.name).Append('(').Append(projection.carrierType)
                        .Append(" value) => ").Append(context).Append(".RecipeConversions.FromNative_").Append(alias.name).AppendLine("(value);");
            }
            text.AppendLine("    }");
        }
        return text.AppendLine("}").ToString();
    }
}
