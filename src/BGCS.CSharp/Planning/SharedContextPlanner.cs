using System;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Core;
using BGCS.Core.Interop;
using BGCS.Core.Requests;
using BGCS.Core.Targets;

namespace BGCS.CSharp.Planning;

internal static class SharedContextPlanner
{
    internal static CSharpSharedContextPlan Plan(
        CSharpEmissionPlan plan,
        EmissionRequest request
    )
        => new(CSharpNames.Identifier(plan.api.name), CSharpNames.Namespace(request.runtimeNamespace),
            request.importMode == BindingImportMode.FunctionTable && (request.managedRuntime is null || request.managedRuntime.moduleMechanism == NativeModuleMechanism.DynamicLibrary),
            request.importMode == BindingImportMode.FunctionTable && request.managedRuntime?.moduleMechanism == NativeModuleMechanism.StaticAdapter,
            CreateMethods(plan, request));

    internal static ImmutableArray<CSharpFacadeMethodPlan> CreateMethods(
        CSharpEmissionPlan plan,
        EmissionRequest request
    ) {
        ImmutableArray<CSharpFacadeMethodPlan>.Builder methods = ImmutableArray.CreateBuilder<CSharpFacadeMethodPlan>();
        var functionsById = plan.api.interop.functions.ToLookup(function => function.id);
        foreach (CSharpMethodPlan method in plan.methods)
        {
            InteropFunction function = functionsById[method.nativeExport].Single();
            if (!OverloadPlanner.IsDirect(plan, method, function)) continue;
            Add(method, method.projectedReturnType,
                Enumerable.Range(0, method.parameterTypes.Length).ToImmutableArray());
        }
        foreach (CSharpStatusMethodPlan status in plan.statusMethods.Where(status => status.instanceIndex < 0
            && !plan.callScopedCallbacks.Any(callback => callback.methodIndex == status.methodIndex)))
        {
            CSharpMethodPlan method = plan.methods[status.methodIndex];
            string result = status.ownedResult is { } owned
                ? owned.objectName is string name ? ContextType(plan) + "." + name : "global::" + CSharpNames.Namespace(request.runtimeNamespace) + ".OwnedNativeHandle"
                : method.valueProjections.TryGetValue(status.resultIndex, out CSharpRecipeProjectionPlan? projection) && projection.fromNativeExpression is not null
                    ? projection.managedType : status.resultType;
            Add(method, result, status.inputIndices);
        }
        foreach (CSharpDirectOwnedFactoryPlan factory in plan.directOwnedFactories)
        {
            CSharpMethodPlan method = plan.methods[factory.methodIndex];
            Add(method, factory.ownership.objectName is string owned ? ContextType(plan) + "." + owned
                : "global::" + CSharpNames.Namespace(request.runtimeNamespace) + ".OwnedNativeHandle",
                Enumerable.Range(0, method.parameterTypes.Length).ToImmutableArray());
        }
        foreach (CSharpModuleBorrowedFactoryPlan factory in plan.directBorrowedFactories)
        {
            CSharpMethodPlan method = plan.methods[factory.methodIndex];
            Add(method, factory.objectName is string borrowed ? ContextType(plan) + "." + borrowed
                : "global::" + CSharpNames.Namespace(request.runtimeNamespace) + ".ModuleBorrowedNativeHandle",
                Enumerable.Range(0, method.parameterTypes.Length).ToImmutableArray());
        }
        methods.AddRange(plan.nullDefaultMethods.Select(operation => operation.signature));
        methods.AddRange(plan.objects.SelectMany(item => item.directMethods).Select(method => method.contextSignature));
        methods.AddRange(plan.stringResults.Select(result => result.signature));
        methods.AddRange(plan.valueStringResults.Select(result => result.signature));
        methods.AddRange(plan.pointerFunctions.Select(operation => operation.signature));
        foreach (CSharpVirtualProxyPlan proxy in plan.virtualProxies)
            methods.Add(new("Create" + proxy.name + "Callbacks", ContextType(plan) + "." + proxy.registrationName,
                [ContextType(plan) + "." + proxy.interfaceName, "global::System.Action<nint>"], ["target", "stopAndJoinNative"]));
        methods.AddRange(plan.callScopedCallbacks.Select(operation => operation.signature));
        methods.AddRange(plan.retainedCallbacks.Select(operation => operation.signature));
        methods.AddRange(plan.asyncOperations.Select(operation => operation.signature));
        methods.AddRange(plan.aliases.Where(alias => alias.friendly is not null).Select(alias => alias.friendly!));
        methods.AddRange(plan.defaultOverloads.Select(overload => overload.signature));
        methods.AddRange(plan.inputVariations.Select(variation => variation.signature));
        return methods.ToImmutable();

        void Add(
            CSharpMethodPlan method,
            string result,
            ImmutableArray<int> inputs
        ) {
            int[] selected = OverloadPlanner.PublicInputs(method, inputs).ToArray();
            methods.Add(new(method.name, result, selected.Select(index => OverloadPlanner.InputType(method, index)).ToImmutableArray(),
                selected.Select(index => method.parameterNames[index]).ToImmutableArray())
            {
                nativeExport = method.nativeExport,
                inputIndices = selected.ToImmutableArray(),
                parameterModifiers = selected.Select(index => OverloadPlanner.InputModifier(method, index)).ToImmutableArray(),
                documentation = method.documentation,
                documentationContent = method.documentationContent is { } content ? content with { parameters = selected.Select(index => content.parameters[index]).ToImmutableArray() } : null
            });
        }
    }

    internal static string ContextType(CSharpEmissionPlan plan) => "global::" + CSharpNames.Namespace(plan.api.@namespace) + "." + plan.contextName;
}
