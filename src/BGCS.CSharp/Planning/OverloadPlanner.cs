using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BGCS.Contracts;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Native;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BGCS.CSharp.Planning;

/// <summary>Rejects conflicting raw and projected parameter signatures before candidate source is written.</summary>
internal static class OverloadPlanner
{
    internal static void Validate(
        CSharpEmissionPlan plan,
        EmissionRequest request
    ) {
        Dictionary<StableId, InteropFunction> nativeExports = plan.api.interop.functions.ToDictionary(function => function.id);
        TypeNames types = new(plan, request.runtimeNamespace);
        Scope raw = new("RawApi", types, ["RawApi", "m_owner"]);
        raw.AddObjectMethods();
        foreach (CSharpMethodPlan method in plan.methods) raw.Add(method.name, method.parameterTypes, method.exportName);
        foreach (CSharpAliasPlan alias in plan.aliases) raw.Add(alias.raw.name, alias.raw.parameterTypes, "managed alias of " + alias.raw.exportName);

        HashSet<string> contextNames = new(StringComparer.Ordinal) { plan.contextName.TrimStart('@'), "RawApi", "Raw", "m_session" };
        if (plan.api.emitLibraryNameConstant && !contextNames.Add(Emission.LibraryNameEmitter.C_NAME))
            throw new ArgumentException("The managed context name conflicts with its public native library name constant.");
        bool temporaryAllocator = Emission.ContextEmitter.HasTemporaryAllocator(plan);
        string[] AllocatorParameters(params string[] prefix) => temporaryAllocator
            ? [.. prefix, Emission.ContextEmitter.TemporaryAllocatorType(request.runtimeNamespace)] : prefix;
        void AddFactory(
            Scope scope,
            string name,
            string[] prefix,
            string origin,
            bool optionalLibraryPath = false
        ) {
            scope.Add(name, AllocatorParameters(prefix), origin);
            if (temporaryAllocator) scope.Add(name, prefix, origin + " with its optional allocator omitted");
            if (optionalLibraryPath) scope.Add(name, [], origin + " with its optional library path omitted");
        }
        if (temporaryAllocator) contextNames.Add("m_temporaryAllocator");
        foreach (CSharpValueStringResultPlan result in plan.valueStringResults.Where(result => result.receiver is not null))
            contextNames.Add(Emission.ValueStringResultEmitter.Helper(result.factory.methodIndex));
        foreach (CSharpFunctionContainerPlan container in plan.functionContainers)
            if (!contextNames.Add(container.name.TrimStart('@')))
                throw new ArgumentException("A configured function container conflicts with a required context member.");
        foreach (CSharpObjectPlan item in plan.objects)
            if (!contextNames.Add(item.name.TrimStart('@')))
                throw new ArgumentException("An owned object type conflicts with a required context member.");
        foreach (CSharpCallbackInputPlan callback in plan.callScopedCallbacks.SelectMany(operation => operation.callbacks).Where(callback => callback.abiDelegateType is null))
            foreach (string name in new[] { callback.delegateName, callback.stateName, callback.thunkName })
                if (!contextNames.Add(name) || plan.storage.Any(storage => storage.name.TrimStart('@') == name) || plan.managedEnums.Any(declaration => declaration.name.TrimStart('@') == name))
                    throw new ArgumentException("A generated managed callback type or helper conflicts with another context or storage member.");
        foreach (CSharpRetainedCallbackPlan operation in plan.retainedCallbacks)
            foreach (string name in new[] { operation.registrationName, operation.nativeCallName, operation.callback.delegateName,
                operation.callback.stateName, operation.callback.thunkName, "__BgcsStop_" + operation.methodIndex })
                if (!contextNames.Add(name)) throw new ArgumentException("A retained callback type or helper conflicts with another context member.");
        foreach (CSharpVirtualProxyPlan proxy in plan.virtualProxies)
        {
            foreach (string name in new[] { proxy.interfaceName, proxy.registrationName, proxy.name + "NativeCall", "__Bgcs" + proxy.name + "CallbackState",
                VirtualProxyEmitterName(proxy, "Create"), VirtualProxyEmitterName(proxy, "Destroy"), VirtualProxyEmitterName(proxy, "Cast") })
                if (!contextNames.Add(name)) throw new ArgumentException("A virtual callback implementation type or helper collides with the context.");
            for (int index = 0; index < proxy.slots.Length; index++) contextNames.Add("__Bgcs" + proxy.name + "CallbackThunk" + index);
            if (plan.methods.Any(method => method.name.TrimStart('@') == "With" + proxy.name + "Callbacks"))
                throw new ArgumentException("A native method conflicts with the generic managed callback scope factory.");
        }
        foreach (CSharpAsyncOperationPlan operation in plan.asyncOperations)
            foreach (string name in new[] { operation.callback.delegateName, operation.callback.stateName, operation.callback.thunkName,
                "__BgcsAsyncStop_" + operation.methodIndex, "__BgcsAsyncCancel_" + operation.methodIndex })
                if (!contextNames.Add(name)) throw new ArgumentException("An async callback state or helper conflicts with another context member.");
        if (plan.importMode == BindingImportMode.FunctionTable)
            for (int index = 0; index < plan.methods.Length; index++) contextNames.Add("m_function" + index.ToString(CultureInfo.InvariantCulture));
        else contextNames.Add("NativeImports");
        if (plan.importMode == BindingImportMode.FunctionTable)
            foreach (CSharpPhysicalImportPlan physical in plan.physicalImports) contextNames.Add(physical.fieldName);
        if (!plan.recipeProjections.IsEmpty) contextNames.Add("RecipeConversions");
        if (!plan.defaultConstants.IsEmpty) contextNames.Add("__BgcsDefaultArguments");
        foreach (CSharpDirectOwnedFactoryPlan factory in plan.directOwnedFactories) contextNames.Add(Emission.DirectOwnedFactoryEmitter.Cleanup(factory.methodIndex));
        foreach (CSharpReceiverMethodPlan receiver in plan.objects.SelectMany(item => item.directMethods)) contextNames.Add(Emission.DirectReceiverEmitter.Helper(receiver.methodIndex));
        if (!plan.constants.IsEmpty || !plan.stringConstants.IsEmpty) contextNames.Add("Constants");
        if (request.managedRuntime?.moduleMechanism == NativeModuleMechanism.StaticAdapter) contextNames.Add("StaticNativeAdapter");
        if (plan.api.nestGeneratedTypesInApi && plan.sharedContext is null) ReserveNestedTypes(contextNames);
        Scope context = new(plan.contextName, types, contextNames);
        context.AddObjectMethods();
        context.Add("Dispose", [], "the context disposal member");
        context.Add("DisposeAsync", [], "the context asynchronous disposal member");
        if (plan.importMode == BindingImportMode.FunctionTable)
        {
            AddFactory(context, "FromProvider", ["global::" + request.runtimeNamespace + ".INativeModuleProvider"], "the provider factory");
            if (request.managedRuntime?.moduleMechanism == NativeModuleMechanism.StaticAdapter)
            {
                AddFactory(context, "FromStatic", [], "the static native adapter factory");
            }
            else if (request.managedRuntime is null || request.managedRuntime.moduleMechanism == NativeModuleMechanism.DynamicLibrary)
                AddFactory(context, "Load", ["string"], "the native module loading factory", optionalLibraryPath: true);
        }
        else AddFactory(context, "Create", [], "the shared-import context factory");
        foreach (CSharpVirtualProxyPlan proxy in plan.virtualProxies)
            context.Add("Create" + proxy.name + "Callbacks", [proxy.interfaceName, "global::System.Action<nint>"], "explicit managed virtual registration");
        foreach (CSharpCallbackMethodPlan operation in plan.callScopedCallbacks)
            context.Add(operation.signature.name, operation.signature.parameterTypes, "explicit call-scoped native callback projection", operation.signature.parameterModifiers);
        foreach (CSharpRetainedCallbackPlan operation in plan.retainedCallbacks)
            context.Add(operation.signature.name, operation.signature.parameterTypes, "explicit native stop-and-join callback registration", operation.signature.parameterModifiers);
        foreach (CSharpAsyncOperationPlan operation in plan.asyncOperations)
            context.Add(operation.signature.name, operation.signature.parameterTypes, "explicit retained native async operation", operation.signature.parameterModifiers);

        foreach (CSharpMethodPlan method in plan.methods)
        {
            if (!nativeExports.TryGetValue(method.nativeExport, out InteropFunction? function))
                throw new InvalidOperationException("An overload shape references a missing native export.");
            if (IsDirect(plan, method, function))
            {
                int[] inputs = PublicInputs(method, Enumerable.Range(0, method.parameterTypes.Length)).ToArray();
                context.Add(method.name, inputs.Select(index => InputType(method, index)), method.exportName, inputs.Select(index => InputModifier(method, index)));
            }
        }
        foreach (CSharpStatusMethodPlan status in plan.statusMethods.Where(status => status.instanceIndex < 0))
        {
            CSharpMethodPlan method = plan.methods[status.methodIndex];
            int[] inputs = PublicInputs(method, status.inputIndices).ToArray();
            context.Add(method.name, inputs.Select(index => InputType(method, index)), method.exportName, inputs.Select(index => InputModifier(method, index)));
        }
        foreach (CSharpDirectOwnedFactoryPlan factory in plan.directOwnedFactories)
        {
            CSharpMethodPlan method = plan.methods[factory.methodIndex];
            int[] inputs = PublicInputs(method, Enumerable.Range(0, method.parameterTypes.Length)).ToArray();
            context.Add(method.name, inputs.Select(index => InputType(method, index)), "the direct owned factory", inputs.Select(index => InputModifier(method, index)));
        }
        foreach (CSharpModuleBorrowedFactoryPlan factory in plan.directBorrowedFactories)
        {
            CSharpMethodPlan method = plan.methods[factory.methodIndex];
            int[] inputs = PublicInputs(method, Enumerable.Range(0, method.parameterTypes.Length)).ToArray();
            context.Add(method.name, inputs.Select(index => InputType(method, index)), "the module-borrowed getter", inputs.Select(index => InputModifier(method, index)));
        }
        foreach (CSharpReceiverMethodPlan member in plan.objects.SelectMany(item => item.directMethods))
            context.Add(member.contextSignature.name, member.contextSignature.parameterTypes, "the explicit C receiver facade", member.contextSignature.parameterModifiers);
        foreach (CSharpStringResultPlan result in plan.stringResults)
            context.Add(result.signature.name, result.signature.parameterTypes, "the native string-result projection", result.signature.parameterModifiers);
        foreach (CSharpValueStringResultPlan result in plan.valueStringResults)
            context.Add(result.signature.name, result.signature.parameterTypes, "the scoped native value-string projection", result.signature.parameterModifiers);
        foreach (CSharpPointerFunctionPlan operation in plan.pointerFunctions)
            context.Add(operation.signature.name, operation.signature.parameterTypes, "the explicit non-owning native address projection", operation.signature.parameterModifiers);
        foreach (CSharpPointerCarrierPlan carrier in plan.pointerCarriers)
        {
            Scope address = new(carrier.name, types, []);
            foreach (CSharpPointerFunctionPlan operation in plan.pointerFunctions.Where(operation => operation.receiverCarrier == carrier.name))
            {
                CSharpFacadeMethodPlan signature = operation.signature;
                int receiver = signature.inputIndices.IndexOf(operation.receiverIndex!.Value);
                int[] inputs = Enumerable.Range(0, signature.parameterTypes.Length).Where(index => index != receiver).ToArray();
                address.Add(signature.name, new[] { "global::" + CSharpNames.Namespace(plan.api.@namespace) + "." + CSharpNames.Identifier(plan.contextName) }
                    .Concat(inputs.Select(index => signature.parameterTypes[index])), "the explicit-context address receiver",
                    new[] { string.Empty }.Concat(inputs.Select(index => FacadeParameters.Modifier(signature, index))));
            }
        }
        foreach (CSharpAliasPlan alias in plan.aliases.Where(alias => alias.friendly is not null))
            context.Add(alias.friendly!.name, alias.friendly.parameterTypes, "managed alias of " + alias.raw.exportName, alias.friendly.parameterModifiers);
        foreach (CSharpNullDefaultMethodPlan operation in plan.nullDefaultMethods)
            context.Add(operation.signature.name, operation.signature.parameterTypes, "the explicitly bound NULL-only operation", operation.signature.parameterModifiers);
        foreach (CSharpDefaultOverloadPlan overload in plan.defaultOverloads)
            context.Add(overload.signature.name, overload.signature.parameterTypes, "explicit default arguments for " + overload.targetName, overload.signature.parameterModifiers);
        foreach (CSharpInputVariationPlan variation in plan.inputVariations)
            context.Add(variation.signature.name, variation.signature.parameterTypes, "explicit input variation for " + variation.targetName, variation.signature.parameterModifiers);
        if (plan.sharedContext is { } shared)
        {
            if (plan.api.emitLibraryNameConstant && shared.name.TrimStart('@') == Emission.LibraryNameEmitter.C_NAME)
                throw new ArgumentException("The managed shared facade name conflicts with its public native library name constant.");
            HashSet<string> facadeNames = new(new[] { shared.name.TrimStart('@'), "m_context", "Current", "Install", "IsInitialized", "Raw", "Constants", "RecipeConversions" }
                .Concat(plan.api.emitLibraryNameConstant ? [Emission.LibraryNameEmitter.C_NAME] : Array.Empty<string>())
                .Concat(plan.functionContainers.Select(container => container.name.TrimStart('@'))), StringComparer.Ordinal);
            if (plan.api.nestGeneratedTypesInApi) ReserveNestedTypes(facadeNames);
            Scope facade = new(shared.name, types, facadeNames);
            facade.AddObjectMethods();
            facade.Add("Dispose", [], "shared context disposal");
            facade.Add("DisposeAsync", [], "shared context asynchronous disposal");
            if (plan.importMode == BindingImportMode.FunctionTable)
            {
                AddFactory(facade, "FromProvider", ["global::" + shared.runtimeNamespace + ".INativeModuleProvider"], "shared provider initialization");
                if (shared.canLoad) AddFactory(facade, "Load", ["string"], "shared dynamic initialization", optionalLibraryPath: true);
                if (shared.canFromStatic) AddFactory(facade, "FromStatic", [], "shared static adapter initialization");
            }
            else AddFactory(facade, "Create", [], "shared import initialization");
            foreach (CSharpFacadeMethodPlan method in shared.methods) facade.Add(method.name, method.parameterTypes, "the friendly forwarding method", method.parameterModifiers);
        }
        foreach (CSharpObjectPlan item in plan.objects)
        {
            Scope instance = new(item.name, types, [item.name.TrimStart('@'), "m_context", "m_handle", "isClosed", "releaseFailure", "releaseDomain", "__BgcsBelongsTo"]);
            instance.AddObjectMethods();
            instance.Add("Dispose", [], "the owned object disposal member");
            instance.Add("AsBorrowed", [], "the borrowed object view factory");
            foreach (int index in item.methodIndices)
            {
                CSharpStatusMethodPlan status = plan.statusMethods[index];
                CSharpMethodPlan method = plan.methods[status.methodIndex];
                int[] inputs = PublicInputs(method, status.inputIndices.Where(index => index != status.instanceIndex)).ToArray();
                instance.Add(method.name, inputs.Select(index => InputType(method, index)), method.exportName, inputs.Select(index => InputModifier(method, index)));
            }
            foreach (CSharpReceiverMethodPlan method in item.directMethods)
                instance.Add(method.objectSignature.name, method.objectSignature.parameterTypes, "the explicit C object member", method.objectSignature.parameterModifiers);
            foreach (CSharpValueStringResultPlan method in plan.valueStringResults.Where(result => result.receiver?.objectName == item.name))
                instance.Add(method.receiver!.signature.name, method.receiver.signature.parameterTypes, "the creating-call text value member", method.receiver.signature.parameterModifiers);
            foreach (CSharpCallbackMethodPlan callback in plan.callScopedCallbacks.Where(operation => operation.receiver?.objectName == item.name))
                instance.Add(callback.receiver!.objectSignature.name, callback.receiver.objectSignature.parameterTypes,
                    "the owned call-scoped callback member", callback.receiver.objectSignature.parameterModifiers);
        }
        foreach (CSharpStoragePlan carrier in plan.storage.Where(storage => !storage.valueMembers.IsEmpty))
        {
            HashSet<string> reserved = carrier.fields.Select(field => field.name.TrimStart('@'))
                .Concat(carrier.bitfields.Select(field => field.name.TrimStart('@')))
                .Concat(carrier.flexibleArrays.Select(field => field.name.TrimStart('@'))).Append(carrier.name.TrimStart('@')).ToHashSet(StringComparer.Ordinal);
            if (carrier.presentation is { } presentation)
            {
                if (presentation.emitSizeConstant) reserved.Add("NativeSize");
                if (presentation.validity is { } validity)
                {
                    reserved.Add(validity.propertyName.TrimStart('@'));
                    if (validity.constantName is not null) reserved.Add(validity.constantName);
                }
            }
            Scope value = new(carrier.name, types, reserved);
            value.AddObjectMethods();
            foreach (CSharpValueReceiverPlan member in carrier.valueMembers)
                value.Add(member.signature.name, member.signature.parameterTypes, "the original-storage value receiver member", member.signature.parameterModifiers);
        }

        void ReserveNestedTypes(HashSet<string> reserved)
        {
            foreach (string name in plan.storage.Select(storage => storage.name)
                .Concat(plan.storage.Select(storage => storage.alignmentProbeName).OfType<string>())
                .Concat(plan.managedEnums.Select(declaration => declaration.name)).Concat(plan.abiDelegates.Select(declaration => declaration.name))
                .Concat(plan.pointerCarriers.Select(carrier => carrier.name)))
                if (!reserved.Add(name.TrimStart('@')))
                    throw new ArgumentException("A nested generated type conflicts with a required public API member: " + name);
        }
        foreach (CSharpFunctionContainerPlan container in plan.functionContainers)
        {
            Scope view = new(container.viewType, types, [container.viewType.TrimStart('@'), "Current", "m_context"]);
            view.AddObjectMethods();
            foreach (CSharpContainerMethodPlan method in container.methods)
                view.Add(method.signature.name, method.signature.parameterTypes, "the checked function container forwarding method", method.signature.parameterModifiers);
        }
    }

    private static string VirtualProxyEmitterName(
        CSharpVirtualProxyPlan proxy,
        string role
    ) => "__Bgcs" + proxy.name + "Callback" + role;

    internal static bool IsDirect(
        CSharpEmissionPlan plan,
        CSharpMethodPlan method,
        InteropFunction function
    ) {
        if (function.id != method.nativeExport) throw new InvalidOperationException("A friendly shape received a different native export identity.");
        if (function.error is not null || function.asynchronous is not null || function.returnOwnership is not null || function.returnString is not null || !SupportsDirectResult(plan, function.returnType)) return false;
        return Enumerable.Range(0, function.parameters.Length).All(index => SupportsDirectInput(plan, method, function, index));
    }

    internal static bool SupportsDirectInput(
        CSharpEmissionPlan plan,
        CSharpMethodPlan method,
        InteropFunction function,
        int index
    ) {
        InteropParameter parameter = function.parameters[index];
        return method.bufferInputs.Any(input => input.parameterIndex == index)
            || method.bufferProtocols.Any(buffer => buffer.writtenCountIndex == index)
            || method.valueParameters.Any(value => value.parameterIndex == index)
            || method.stringInputs.Any(input => input.parameterIndex == index)
            || SupportsDirectResult(plan, parameter.type) && parameter.ownership is null && parameter.callback is null && parameter.text is null && parameter.buffer is null && parameter.singleValue is null;
    }

    internal static bool SupportsDirectResult(
        CSharpEmissionPlan plan,
        StableId id
    ) {
        InteropType type = plan.api.interop.GetType(id);
        while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified) type = plan.api.interop.GetType(type.elementType!.Value);
        return type.category is NativeTypeCategory.Primitive or NativeTypeCategory.Enumeration
            || plan.aggregateCalls.Any(aggregate => aggregate.nativeType == type.id);
    }

    internal static IEnumerable<int> PublicInputs(
        CSharpMethodPlan method,
        IEnumerable<int> inputs
    ) {
        HashSet<int> lengths = method.bufferInputs.Select(input => input.lengthIndex).ToHashSet();
        lengths.UnionWith(method.stringInputs.Where(input => input.lengthIndex is not null).Select(input => input.lengthIndex!.Value));
        return inputs.Where(index => !lengths.Contains(index));
    }

    internal static string InputType(
        CSharpMethodPlan method,
        int index
    ) {
        if (method.bufferProtocols.Any(buffer => buffer.writtenCountIndex == index)) return "int";
        CSharpBufferInputPlan? buffer = method.bufferInputs.SingleOrDefault(input => input.parameterIndex == index);
        if (buffer is not null) return "global::System." + (buffer.direction == NativeParameterDirection.Input ? "ReadOnlySpan" : "Span") + "<" + buffer.elementType + ">";
        CSharpStringInputPlan? text = method.stringInputs.SingleOrDefault(input => input.parameterIndex == index);
        if (text is not null) return text.allowsNull ? "string?" : "string";
        return method.valueProjections.TryGetValue(index, out CSharpRecipeProjectionPlan? projection) && (projection.toNativeExpression is not null
            || method.valueParameters.Any(value => value.parameterIndex == index && value.direction == NativeParameterDirection.Output) && projection.fromNativeExpression is not null)
            ? projection.managedType : method.valueParameters.SingleOrDefault(value => value.parameterIndex == index)?.managedElementType ?? method.parameterTypes[index];
    }

    internal static string InputModifier(
        CSharpMethodPlan method,
        int index
    )
        => method.bufferProtocols.Any(buffer => buffer.writtenCountIndex == index) ? "out"
            : method.valueParameters.SingleOrDefault(value => value.parameterIndex == index) is { } value
                ? value.direction switch { NativeParameterDirection.Input => "in", NativeParameterDirection.Output => "out", _ => "ref" } : string.Empty;

    internal static string InputDeclaration(
        CSharpMethodPlan method,
        int index
    )
        => (InputModifier(method, index) is { Length: > 0 } modifier ? modifier + " " : string.Empty) + InputType(method, index) + " " + method.parameterNames[index];

    private sealed class Scope(
        string name,
        TypeNames types,
        IEnumerable<string> reserved
    )
    {
        private readonly HashSet<string> m_reserved = new(reserved, StringComparer.Ordinal);
        private readonly Dictionary<string, string> m_signatures = new(StringComparer.Ordinal);

        internal void Add(
            string method,
            IEnumerable<string> parameters,
            string origin,
            IEnumerable<string>? parameterModifiers = null
        ) {
            string identifier = CSharpNames.Identifier(method).TrimStart('@');
            if (m_reserved.Contains(identifier)) throw new InvalidOperationException($"Managed member '{name}.{identifier}' conflicts with a required type, field or property; declare an explicit name mapping.");
            string[] normalized = parameters.Select(types.Normalize).ToArray();
            string[] modifiers = parameterModifiers?.ToArray() ?? [];
            if (modifiers.Length != 0 && modifiers.Length != normalized.Length) throw new InvalidOperationException("An overload signature lost its reference modifiers.");
            for (int index = 0; index < modifiers.Length; index++)
            {
                if (modifiers[index] is not ("" or "in" or "ref" or "out")) throw new InvalidOperationException("An overload signature contains an unknown reference modifier.");
                if (modifiers[index].Length != 0) normalized[index] = "byref:" + normalized[index];
            }
            string signature = identifier + "(" + string.Join(",", normalized) + ")";
            if (m_signatures.TryGetValue(signature, out string? previous))
                throw new InvalidOperationException($"Managed overload '{name}.{signature}' is produced by both '{previous}' and '{origin}'. Return types and reference nullability do not distinguish overloads; declare explicit name mappings.");
            m_signatures.Add(signature, origin);
        }

        internal void AddObjectMethods()
        {
            Add("GetType", [], "System.Object.GetType");
            Add("GetHashCode", [], "System.Object.GetHashCode");
            Add("ToString", [], "System.Object.ToString");
            Add("Equals", ["object"], "System.Object.Equals");
            Add("Equals", ["object", "object"], "System.Object.Equals");
            Add("ReferenceEquals", ["object", "object"], "System.Object.ReferenceEquals");
        }
    }

    private sealed class TypeNames(
        CSharpEmissionPlan plan,
        string runtimeNamespace
    )
    {
        private readonly HashSet<string> m_values = plan.storage.Select(storage => storage.name.TrimStart('@'))
            .Concat(plan.managedEnums.Select(declaration => declaration.name.TrimStart('@')))
            .Concat(plan.pointerCarriers.Select(carrier => carrier.name.TrimStart('@'))).ToHashSet(StringComparer.Ordinal);
        private readonly HashSet<string> m_references = plan.objects.Select(item => plan.api.@namespace + "." + plan.contextName.TrimStart('@') + "." + item.name.TrimStart('@'))
            .Concat(plan.callScopedCallbacks.SelectMany(operation => operation.callbacks).Where(callback => callback.abiDelegateType is null)
                .Select(callback => plan.api.@namespace + "." + plan.contextName.TrimStart('@') + "." + callback.delegateName.TrimStart('@')))
            .Concat(plan.abiDelegates.Select(declaration => CSharpGeneratedTypeScope.DeclarationNamespace(plan.api).Replace("@", string.Empty, StringComparison.Ordinal) + "." + declaration.name.TrimStart('@')))
            .Concat(plan.retainedCallbacks.SelectMany(operation => new[] { operation.registrationName, operation.callback.delegateName })
                .Select(name => plan.api.@namespace + "." + plan.contextName.TrimStart('@') + "." + name.TrimStart('@')))
            .Append(runtimeNamespace + ".INativeModuleProvider").ToHashSet(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> s_aliases = new(StringComparer.Ordinal)
        {
            ["bool"] = "System.Boolean",
            ["byte"] = "System.Byte",
            ["sbyte"] = "System.SByte",
            ["short"] = "System.Int16",
            ["ushort"] = "System.UInt16",
            ["int"] = "System.Int32",
            ["uint"] = "System.UInt32",
            ["long"] = "System.Int64",
            ["ulong"] = "System.UInt64",
            ["char"] = "System.Char",
            ["float"] = "System.Single",
            ["double"] = "System.Double",
            ["decimal"] = "System.Decimal",
            ["nint"] = "System.IntPtr",
            ["nuint"] = "System.UIntPtr",
            ["object"] = "System.Object",
            ["dynamic"] = "System.Object",
            ["string"] = "System.String",
            ["void"] = "System.Void"
        };

        internal string Normalize(string type) => Normalize(SyntaxFactory.ParseTypeName(type, options: new CSharpParseOptions(LanguageVersion.CSharp14)));

        private string Normalize(TypeSyntax type) => type switch
        {
            PredefinedTypeSyntax keyword => s_aliases[keyword.Keyword.ValueText],
            IdentifierNameSyntax identifier => s_aliases.TryGetValue(identifier.Identifier.ValueText, out string? known) ? known
                : m_values.Contains(identifier.Identifier.ValueText) ? CSharpGeneratedTypeScope.DeclarationNamespace(plan.api).Replace("@", string.Empty, StringComparison.Ordinal) + "." + identifier.Identifier.ValueText
                : plan.abiDelegates.Any(declaration => declaration.name.TrimStart('@') == identifier.Identifier.ValueText) ? CSharpGeneratedTypeScope.DeclarationNamespace(plan.api).Replace("@", string.Empty, StringComparison.Ordinal) + "." + identifier.Identifier.ValueText
                : plan.objects.Any(item => item.name.TrimStart('@') == identifier.Identifier.ValueText)
                    || plan.callScopedCallbacks.SelectMany(operation => operation.callbacks).Any(callback => callback.delegateName.TrimStart('@') == identifier.Identifier.ValueText)
                    || plan.retainedCallbacks.Any(operation => operation.registrationName.TrimStart('@') == identifier.Identifier.ValueText || operation.callback.delegateName.TrimStart('@') == identifier.Identifier.ValueText)
                    ? plan.api.@namespace + "." + plan.contextName.TrimStart('@') + "." + identifier.Identifier.ValueText
                : identifier.Identifier.ValueText,
            GenericNameSyntax generic => generic.Identifier.ValueText + "<" + string.Join(",", generic.TypeArgumentList.Arguments.Select(Normalize)) + ">",
            QualifiedNameSyntax qualified => Name(qualified.Left) + "." + Name(qualified.Right),
            AliasQualifiedNameSyntax alias when alias.Alias.Identifier.ValueText == "global" => Name(alias.Name),
            NullableTypeSyntax nullable when Reference(Normalize(nullable.ElementType)) => Normalize(nullable.ElementType),
            NullableTypeSyntax nullable when Value(Normalize(nullable.ElementType)) => "System.Nullable<" + Normalize(nullable.ElementType) + ">",
            NullableTypeSyntax nullable => Normalize(nullable.ElementType) + "?",
            PointerTypeSyntax pointer => Normalize(pointer.ElementType) + "*",
            ArrayTypeSyntax array => Normalize(array.ElementType) + string.Concat(array.RankSpecifiers.Select(rank => "[" + new string(',', rank.Rank - 1) + "]")),
            TupleTypeSyntax tuple => "System.ValueTuple<" + string.Join(",", tuple.Elements.Select(element => Normalize(element.Type))) + ">",
            _ => type.ToString().Replace("global::", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal)
        };

        private string Name(NameSyntax name) => name switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            GenericNameSyntax generic => generic.Identifier.ValueText + "<" + string.Join(",", generic.TypeArgumentList.Arguments.Select(Normalize)) + ">",
            QualifiedNameSyntax qualified => Name(qualified.Left) + "." + Name(qualified.Right),
            AliasQualifiedNameSyntax alias when alias.Alias.Identifier.ValueText == "global" => Name(alias.Name),
            _ => name.ToString().Replace("global::", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal)
        };

        private bool Reference(string type) => type is "System.String" or "System.Object" || m_references.Contains(type) || type.EndsWith(']')
            || new[] { "System.Collections.Generic.List<", "System.Collections.Generic.Dictionary<", "System.Collections.Generic.HashSet<",
                "System.Collections.Generic.SortedSet<", "System.Collections.Generic.SortedList<", "System.Collections.Generic.SortedDictionary<",
                "System.Collections.Generic.IEnumerable<", "System.Collections.Generic.IEnumerator<", "System.Collections.Generic.ICollection<",
                "System.Collections.Generic.IList<", "System.Collections.Generic.ISet<", "System.Collections.Generic.IDictionary<",
                "System.Collections.Generic.IReadOnly", "System.Func<", "System.Action<" }.Any(prefix => type.StartsWith(prefix, StringComparison.Ordinal));

        private bool Value(string type) => s_aliases.Values.Contains(type, StringComparer.Ordinal) && type is not ("System.String" or "System.Object" or "System.Void")
            || m_values.Contains(type) || m_values.Contains(type.StartsWith(CSharpGeneratedTypeScope.DeclarationNamespace(plan.api) + ".", StringComparison.Ordinal)
                ? type[(CSharpGeneratedTypeScope.DeclarationNamespace(plan.api).Length + 1)..] : type)
            || type.StartsWith("System.ValueTuple<", StringComparison.Ordinal) || type.StartsWith("System.Nullable<", StringComparison.Ordinal)
            || type.StartsWith("System.Collections.Generic.KeyValuePair<", StringComparison.Ordinal)
            || type.StartsWith("System.Span<", StringComparison.Ordinal) || type.StartsWith("System.ReadOnlySpan<", StringComparison.Ordinal)
            || type.StartsWith("System.Memory<", StringComparison.Ordinal) || type.StartsWith("System.ReadOnlyMemory<", StringComparison.Ordinal);
    }
}
