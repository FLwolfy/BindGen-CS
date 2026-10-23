using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using BGCS.Core;
using BGCS.Core.Requests;
using BGCS.Core.Targets;
using BGCS.CSharp.Planning;
using Microsoft.CodeAnalysis.CSharp;

namespace BGCS.CSharp.Emission;

internal static class ContextEmitter
{
    internal static string Emit(
        CSharpEmissionPlan plan,
        EmissionRequest request
    )
        => Emit(plan, request.libraryName);

    internal static string Emit(
        CSharpEmissionPlan plan,
        string libraryName
    ) {
        string name = CSharpNames.Identifier(plan.contextName);
        string runtime = "global::" + CSharpNames.Namespace(plan.runtimeNamespace);
        StringBuilder text = new();
        text.Append("public sealed unsafe partial class ").Append(name).AppendLine(" : global::System.IDisposable, global::System.IAsyncDisposable").AppendLine("{");
        text.Append(LibraryNameEmitter.Emit(plan, libraryName));
        text.Append("    private readonly ").Append(runtime).AppendLine(".NativeModuleSession m_session;");
        bool allocator = HasTemporaryAllocator(plan);
        bool dispatcher = HasCallbackDispatcher(plan);
        if (dispatcher) text.Append("    private readonly ").Append(runtime).AppendLine(".NativeCallbackDispatcher m_callbackDispatcher;");
        if (allocator) text.Append("    private readonly ").Append(runtime).AppendLine(".NativeAllocator? m_temporaryAllocator;");
        if (plan.importMode == BindingImportMode.FunctionTable)
            for (int index = 0; index < plan.methods.Length; index++)
            {
                CSharpPhysicalImportPlan? physical = PhysicalImportPlanner.Find(plan, index);
                text.Append("    private readonly ").Append(PhysicalImportEmitter.FunctionPointer(plan.methods[index], physical)).Append(' ')
                    .Append(physical?.fieldName ?? "m_function" + index).AppendLine(";");
            }
        if (plan.importMode == BindingImportMode.FunctionTable)
            foreach (CSharpPhysicalImportPlan physical in plan.physicalImports)
                text.Append(PhysicalImportEmitter.Forwarder(plan, physical, table: true));
        text.AppendLine("    public RawApi Raw { get; }");
        List<string> constructorParameters = [];
        if (plan.importMode == BindingImportMode.FunctionTable) constructorParameters.Add(runtime + ".INativeModuleProvider provider");
        if (dispatcher) constructorParameters.Add(runtime + ".NativeCallbackDispatcher callbackDispatcher");
        if (allocator) constructorParameters.Add(TemporaryAllocatorParameter(plan.runtimeNamespace));
        text.Append("    private ").Append(name).Append('(').Append(string.Join(", ", constructorParameters));
        text.AppendLine(")").AppendLine("    {");
        text.Append("        m_session = new ").Append(runtime).Append(".NativeModuleSession(");
        text.Append(plan.importMode == BindingImportMode.FunctionTable ? "provider" : "new " + runtime + ".StaticModuleProvider(global::System.Array.Empty<global::System.Collections.Generic.KeyValuePair<string, nint>>())").AppendLine(");");
        text.AppendLine("        try").AppendLine("        {").Append(AbiStorageEmitter.EmitGuards(plan));
        if (dispatcher) text.AppendLine("            m_callbackDispatcher = callbackDispatcher ?? throw new global::System.ArgumentNullException(nameof(callbackDispatcher));");
        if (plan.importMode == BindingImportMode.FunctionTable)
        {
            text.Append("            var symbols = m_session.ResolveRequiredSymbols(new string[] { ")
                .Append(string.Join(", ", plan.methods.Select(method => Literal(method.exportName)))).AppendLine(" });");
            for (int index = 0; index < plan.methods.Length; index++)
            {
                CSharpPhysicalImportPlan? physical = PhysicalImportPlanner.Find(plan, index);
                text.Append("            ").Append(physical?.fieldName ?? "m_function" + index).Append(" = (")
                    .Append(PhysicalImportEmitter.FunctionPointer(plan.methods[index], physical)).Append(")symbols[")
                    .Append(Literal(plan.methods[index].exportName)).AppendLine("];");
            }
        }
        if (allocator)
            text.AppendLine("            if (temporaryAllocatorFactory is not null)")
                .AppendLine("            {")
                .AppendLine("                m_temporaryAllocator = temporaryAllocatorFactory(m_session)")
                .AppendLine("                    ?? throw new global::System.InvalidOperationException(\"The temporary allocator factory returned null.\");")
                .AppendLine("                if (!m_temporaryAllocator.IsBoundTo(m_session))")
                .AppendLine("                    throw new global::System.InvalidOperationException(\"A temporary allocator must retain this exact context session through matching cleanup.\");")
                .AppendLine("            }");
        text.AppendLine("            Raw = new RawApi(this);").AppendLine("        }").AppendLine("        catch").AppendLine("        {")
            .AppendLine("            m_session.Dispose();").AppendLine("            throw;").AppendLine("        }").AppendLine("    }");
        if (plan.importMode == BindingImportMode.FunctionTable)
        {
            text.Append("    public static ").Append(name).Append(" FromProvider(").Append(runtime).Append(".INativeModuleProvider provider")
                .Append(dispatcher ? ", " + runtime + ".NativeCallbackDispatcher callbackDispatcher" : string.Empty)
                .Append(allocator ? ", " + TemporaryAllocatorParameter(plan.runtimeNamespace) : string.Empty).Append(") => new(provider")
                .Append(dispatcher ? ", callbackDispatcher" : string.Empty)
                .Append(allocator ? ", temporaryAllocatorFactory" : string.Empty).AppendLine(");");
            if (plan.managedRuntime?.moduleMechanism == NativeModuleMechanism.StaticAdapter)
                text.Append(StaticContextEmitter.Emit(plan, libraryName));
            else if (plan.managedRuntime?.moduleMechanism == NativeModuleMechanism.DynamicLibrary)
                text.Append("    public static ").Append(name).Append(" Load(")
                    .Append(dispatcher ? runtime + ".NativeCallbackDispatcher callbackDispatcher, " : string.Empty)
                    .Append("string libraryPath = ").Append(Literal(libraryName))
                    .Append(allocator ? ", " + TemporaryAllocatorParameter(plan.runtimeNamespace) : string.Empty).Append(") => new(new ")
                    .Append(runtime).Append(".DynamicModuleProvider(libraryPath, typeof(").Append(name).Append(").Assembly)")
                    .Append(dispatcher ? ", callbackDispatcher" : string.Empty)
                    .Append(allocator ? ", temporaryAllocatorFactory" : string.Empty).AppendLine(");");
        }
        else
        {
            text.AppendLine("    // Imports use the runtime's shared native loader; a context owns call admission, not that loader's handle.");
            text.Append("    public static ").Append(name).Append(" Create(").Append(string.Join(", ", constructorParameters))
                .Append(") => new(").Append(string.Join(", ", new[] { dispatcher ? "callbackDispatcher" : null, allocator ? "temporaryAllocatorFactory" : null }.OfType<string>())).AppendLine(");");
        }
        text.AppendLine("    public void Dispose() => m_session.Dispose();")
            .AppendLine("    public global::System.Threading.Tasks.ValueTask DisposeAsync() => m_session.DisposeAsync();")
            .AppendLine("}");
        return text.ToString();
    }

    internal static string FunctionPointer(CSharpMethodPlan method) => "delegate* unmanaged[" + method.callingConvention + "]<"
        + string.Join(", ", method.parameterTypes.Append(method.returnType)) + ">";

    internal static string Literal(string value) => SymbolDisplay.FormatLiteral(value, true);

    internal static bool HasTemporaryAllocator(CSharpEmissionPlan plan) => plan.methods.Any(method => !method.stringInputs.IsDefaultOrEmpty);

    internal static bool HasCallbackDispatcher(CSharpEmissionPlan plan)
        => plan.retainedCallbacks.Any(callback => callback.nonBlockingShutdown) || plan.asyncOperations.Any(operation => operation.nonBlockingShutdown);

    internal static string TemporaryAllocatorType(string runtimeNamespace)
    {
        string runtime = "global::" + CSharpNames.Namespace(runtimeNamespace);
        return "global::System.Func<" + runtime + ".NativeModuleSession, " + runtime + ".NativeAllocator>";
    }

    internal static string TemporaryAllocatorParameter(string runtimeNamespace)
        => TemporaryAllocatorType(runtimeNamespace) + "? temporaryAllocatorFactory = null";
}
