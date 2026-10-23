using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Text.RegularExpressions;
using BGCS.Analysis.Interop;
using BGCS.Analysis.Bridge;
using BGCS.Analysis.Types;
using BGCS.Core.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.NativeBridge.Planning;
using BGCS.NativeBridge.Recipes;

namespace BGCS.NativeBridge.C;

internal sealed class AggregatePointerLowering(
    LoweringRegistry lowerings,
    CallableRecipeRegistry callableRecipes
)
{
    internal NativeBridgePlan Plan(
        LoweringContext context,
        IEnumerable<StableId>? selected,
        CancellationToken cancellationToken,
        IEnumerable<NativeVariadicAdapter>? variadicAdapters = null
    ) {
        NativeModule module = context.module;
        if (!Regex.IsMatch(context.exportPrefix, "^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)
            || context.exportPrefix.Contains("__", StringComparison.Ordinal))
            throw new ArgumentException("A bridge prefix must be an unreserved C identifier.");
        if (module.targetId != context.target.targetId.value || module.triple != context.target.triple
            || new NativeModuleVerifier().Validate(module).Any(diagnostic => diagnostic.isFailure))
            throw new InvalidOperationException("Native lowering requires a verified graph for its exact target.");
        NativeTypeFormatter formatter = new();
        Dictionary<StableId, BridgeType> types = [];
        List<BridgeExport> exports = [];
        List<AggregateCallbackAdapter> callbacks = [];
        Dictionary<ContentDigest, ControlledSourceFragment> fragments = [];
        Dictionary<StableId, NativeTypeLoweringPlan?> customTypes = [];
        HashSet<StableId> resolvingCustom = [];
        HashSet<StableId>? selectedIds = selected?.ToHashSet();
        NativeVariadicAdapter[] finiteAdapters = variadicAdapters?.ToArray() ?? [];
        if (finiteAdapters.Select(adapter => adapter.wrapper).Distinct(StringComparer.Ordinal).Count() != finiteAdapters.Length)
            throw new InvalidOperationException("Finite C adapters require unique wrapper identities.");
        foreach (NativeVariadicAdapter adapter in finiteAdapters)
        {
            NativeDeclaration original = module.GetDeclaration(adapter.sourceDeclaration);
            if (!original.isVariadic || original.kind != "Function" || !original.hasPrototype
                || adapter.argumentTypes.IsDefault || adapter.additionalArguments.IsDefault
                || adapter.argumentTypes.Length != adapter.additionalArguments.Length
                || adapter.fixedParameterCount != original.children.Select(module.GetDeclaration).Count(item => item.kind == "Parameter"))
                throw new InvalidOperationException("A finite C adapter lost its exact original variadic declaration or first-parse argument facts.");
            foreach (StableId type in adapter.argumentTypes) module.GetType(type);
        }
        foreach ((NativeDeclaration callable, NativeVariadicAdapter? finite) in module.declarations.Where(item => item.kind == "Function")
            .Where(item => (selectedIds is null ? !item.isSystemDeclaration : selectedIds.Contains(item.id)) || finiteAdapters.Any(adapter => adapter.sourceDeclaration == item.id))
            .OrderBy(item => item.id.value, StringComparer.Ordinal).SelectMany(item => item.isVariadic && finiteAdapters.Any(adapter => adapter.sourceDeclaration == item.id)
                ? finiteAdapters.Where(adapter => adapter.sourceDeclaration == item.id).OrderBy(adapter => adapter.wrapper, StringComparer.Ordinal)
                    .Select(adapter => (item, (NativeVariadicAdapter?)adapter))
                : [(item, (NativeVariadicAdapter?)null)]))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (callable.isVariadic && finite is null || !callable.hasPrototype || callable.returnType is null)
                throw new NotSupportedException($"Native bridge '{callable.qualifiedName}' requires a fixed prototype; declare a finite variadic adapter first.");
            CallableLoweringRecipe? callableRecipe = callableRecipes.Match(callable);
            if (callableRecipe?.exclude == true) continue;
            if (callableRecipe is not null) foreach (ControlledSourceFragment fragment in callableRecipe.sourceFragments) AddFragment(fragment);
            NativeCallableLoweringPlan? custom = finite is null ? lowerings.LowerCallable(callable, context, cancellationToken) : null;
            if (custom is not null)
            {
                foreach (BridgeType type in custom.types) Register(type);
                exports.Add(CallableRecipeRegistry.Apply(custom.export, callableRecipe));
                exports.AddRange(custom.additionalExports);
                foreach (ControlledSourceFragment fragment in custom.sourceFragments) AddFragment(fragment);
                continue;
            }
            NativeDeclaration[] original = callable.children.Select(module.GetDeclaration).Where(item => item.kind == "Parameter").ToArray();
            ImmutableArray<BridgeParameter>.Builder parameters = ImmutableArray.CreateBuilder<BridgeParameter>();
            ImmutableArray<BridgeArgumentGroup>.Builder groups = ImmutableArray.CreateBuilder<BridgeArgumentGroup>();
            for (int index = 0; index < original.Length; index++)
            {
                NativeDeclaration parameter = original[index];
                StableId nativeId = parameter.type ?? throw new InvalidOperationException("A native parameter has no adjusted type.");
                NativeTypeLoweringPlan? customType = Custom(nativeId);
                if (customType is { parameterBindings.IsEmpty: false })
                {
                    if (customType.parameterConversion != BridgeConversionKind.SourceExpression || customType.parameterExpression is null)
                        throw new InvalidOperationException("Multiple C carriers require an explicit typed argument expression.");
                    var bindings = ImmutableArray.CreateBuilder<BridgeArgumentBinding>();
                    foreach (CppAbiInput input in customType.parameterBindings)
                    {
                        StableId inputId = new("native-bridge-parameter:" + parameter.id.value + ":abi:" + input.name);
                        BridgeType carrier = types[input.type];
                        parameters.Add(new(inputId, "arg" + index + "_" + input.name, input.type, nativeId,
                            carrier.kind == BridgeTypeKind.Pointer ? BridgeConversionKind.PointerCast : BridgeConversionKind.ScalarCast));
                        bindings.Add(new(input.name, inputId));
                    }
                    groups.Add(new(parameter.id, nativeId, bindings.ToImmutable(), customType.parameterExpression));
                    continue;
                }
                BridgeType storage = Storage(nativeId);
                NativeType function = Resolve(nativeId);
                if (function.category == NativeTypeCategory.Pointer && function.elementType is StableId pointee)
                    function = Resolve(pointee);
                if (function.category == NativeTypeCategory.Function && HasAggregate(function))
                    storage = Callback(callable, parameter, function);
                bool aggregate = customType is null && IsAggregate(nativeId);
                if (aggregate) storage = Pointer(storage, isConst: true);
                BridgeParameter bridgeParameter = new(new("native-bridge-parameter:" + (finite is null ? string.Empty : finite.wrapper + ":") + parameter.id.value), "arg" + index,
                    storage.id, nativeId, customType?.parameterConversion ?? (aggregate ? BridgeConversionKind.Dereference : BridgeConversionKind.PointerCast))
                { conversionExpression = customType?.parameterExpression, managedProjection = customType?.managedProjection };
                parameters.Add(bridgeParameter);
                groups.Add(new(parameter.id, nativeId, [new("value", bridgeParameter.id)], null));
            }
            if (finite is not null)
            {
                if (groups.Any(group => group.inputs.Length != 1 || group.expression is not null))
                    throw new NotSupportedException("Finite C adapters with expanded fixed inputs need one explicit joint callable lowering.");
                for (int index = 0; index < finite.argumentTypes.Length; index++)
                {
                    StableId nativeId = finite.argumentTypes[index];
                    NativeTypeLoweringPlan? conversion = Custom(nativeId);
                    if (conversion is { parameterBindings.IsEmpty: false })
                        throw new NotSupportedException("Finite C variadic arguments with multiple carriers need one explicit joint callable lowering.");
                    BridgeType storage = Storage(nativeId);
                    bool aggregate = conversion is null && IsAggregate(nativeId);
                    if (aggregate) storage = Pointer(storage, true);
                    parameters.Add(new(new("native-bridge-finite:" + finite.wrapper + ":argument:" + index), finite.additionalArguments[index].name,
                        storage.id, nativeId, conversion?.parameterConversion ?? (aggregate ? BridgeConversionKind.Dereference : BridgeConversionKind.PointerCast))
                    { conversionExpression = conversion?.parameterExpression, managedProjection = conversion?.managedProjection });
                }
            }
            NativeType result = Resolve(callable.returnType.Value);
            NativeTypeLoweringPlan? customResult = Custom(callable.returnType.Value);
            if (customResult?.ownedResultRelease is not null || customResult?.resultConversion == BridgeConversionKind.OwnedValue)
                throw new NotSupportedException("A custom C owned result requires an explicit callable lowering with its matching release protocol.");
            // Outer result qualifiers belong to the returned value, not the bridge-owned writable result channel.
            BridgeExport planned = new(new("native-bridge-export:" + callable.id.value + (finite is null ? string.Empty : ":finite:" + finite.wrapper)), ExportName(callable) + (finite is null ? string.Empty : "_" + finite.wrapper), parameters.ToImmutable(),
                new(finite is null ? BridgeOperationKind.FreeCall : BridgeOperationKind.FiniteVariadicCall, callable.id, null, null)
                { argumentGroups = finite is null ? groups.ToImmutable() : [], finiteVariadic = finite })
            {
                resultType = result.primitiveKind == "Void" ? null : Storage(customResult is null && result.category == NativeTypeCategory.Record ? result.id : callable.returnType.Value).id,
                nativeResultType = callable.returnType,
                resultInOutputParameter = customResult is null && IsAggregate(callable.returnType.Value),
                usesErrorChannel = false,
                resultConversion = customResult?.resultConversion ?? BridgeConversionKind.ScalarCast,
                resultExpression = customResult?.resultExpression,
                managedResultProjection = customResult?.managedProjection,
                documentation = callable.documentation
            };
            exports.Add(CallableRecipeRegistry.Apply(planned, callableRecipe));
        }
        foreach (AggregateCallbackAdapter adapter in callbacks.ToArray())
        {
            NativeDeclaration callable = module.GetDeclaration(adapter.sourceFunction);
            // Replacing a shared state channel also changes every callback using it.
            // Unknown relationships cannot safely be guessed from parameter names.
            foreach (NativeDeclaration parameter in callable.children.Select(module.GetDeclaration).Where(item => item.kind == "Parameter"))
            {
                NativeType type = Resolve(parameter.type!.Value);
                if (type.category == NativeTypeCategory.Pointer && type.elementType is StableId pointee) type = Resolve(pointee);
                if (type.category != NativeTypeCategory.Function || parameter.id == adapter.sourceParameter) continue;
                InteropParameter? semantics = context.semantics?.functions.Single(function => function.id == callable.id)
                    .parameters.SingleOrDefault(item => item.id == parameter.id);
                if (semantics?.callback?.userDataParameter == adapter.userDataParameter
                    && !callbacks.Any(item => item.sourceFunction == callable.id && item.sourceParameter == parameter.id))
                {
                    BridgeType normalized = Callback(callable, parameter, type);
                    int exportIndex = exports.FindIndex(export => export.operation.declaration == callable.id);
                    BridgeExport existing = exports[exportIndex];
                    int parameterIndex = Array.FindIndex(callable.children.Select(module.GetDeclaration).Where(item => item.kind == "Parameter").ToArray(), item => item.id == parameter.id);
                    if (existing.parameters.Length != callable.children.Select(module.GetDeclaration).Count(item => item.kind == "Parameter"))
                        throw new NotSupportedException("A shared callback context with expanded parameters requires an explicit joint callable lowering.");
                    exports[exportIndex] = existing with { parameters = existing.parameters.SetItem(parameterIndex, existing.parameters[parameterIndex] with { type = normalized.id }) };
                }
            }
        }
        return CallbackContextPlanner.Apply(new(module, context.exportPrefix, context.headers, types.Values.OrderBy(type => type.id.value, StringComparer.Ordinal).ToImmutableArray(),
            exports.ToImmutableArray())
        { aggregateCallbacks = callbacks.ToImmutableArray(), sourceFragments = fragments.Values.OrderBy(fragment => fragment.origin, StringComparer.Ordinal).ThenBy(fragment => fragment.digest.value, StringComparer.Ordinal).ToImmutableArray(), sourceConventions = context.target.sourceConventions, sourceSemantics = context.semantics });

        BridgeType Storage(StableId id)
        {
            StableId key = new("native-bridge-storage:" + id.value);
            if (types.TryGetValue(key, out BridgeType? known)) return known;
            NativeTypeLoweringPlan? custom = Custom(id);
            if (custom is not null) return types[custom.type];
            BridgeType result = new(key, BridgeTypeKind.NativeStorage, formatter.Format(module, id)) { nativeType = id };
            Register(result);
            return result;
        }
        NativeTypeLoweringPlan? Custom(StableId id)
        {
            if (customTypes.TryGetValue(id, out NativeTypeLoweringPlan? known)) return known;
            if (!resolvingCustom.Add(id)) throw new InvalidOperationException("A custom C type lowering contains a cyclic alias graph.");
            try
            {
                NativeTypeLoweringPlan? custom = lowerings.LowerType(new(module, id), context, cancellationToken);
                NativeType native = module.GetType(id);
                if (custom is null && native.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified && native.elementType is StableId element)
                    custom = Custom(element);
                if (custom is not null)
                {
                    if (custom.types.IsDefault || custom.parameterBindings.IsDefault || custom.sourceFragments.IsDefault)
                        throw new InvalidOperationException("A custom C type lowering returned uninitialized dependency collections.");
                    foreach (BridgeType type in custom.types) Register(type);
                    if (!types.ContainsKey(custom.type) || custom.parameterBindings.Any(binding => !types.ContainsKey(binding.type)))
                        throw new InvalidOperationException("A custom C type lowering omitted a declared carrier.");
                    foreach (ControlledSourceFragment fragment in custom.sourceFragments) AddFragment(fragment);
                }
                customTypes.Add(id, custom);
                return custom;
            }
            finally { resolvingCustom.Remove(id); }
        }
        BridgeType Pointer(
            BridgeType element,
            bool isConst
        ) {
            StableId key = new("native-bridge-pointer:" + element.id.value + (isConst ? ":const" : ":mutable"));
            if (types.TryGetValue(key, out BridgeType? known)) return known;
            BridgeType pointer = new(key, BridgeTypeKind.Pointer, string.Empty) { elementType = element.id, isConst = isConst };
            Register(pointer);
            return pointer;
        }
        BridgeType Callback(
            NativeDeclaration callable,
            NativeDeclaration parameter,
            NativeType function
        ) {
            CallbackContract contract = context.semantics?.functions.Single(item => item.id == callable.id)
                .parameters.Single(item => item.id == parameter.id).callback
                ?? throw new NotSupportedException($"Aggregate callback '{callable.qualifiedName}:{parameter.name}' needs an explicit context and lifetime protocol.");
            if (contract.retained is null || contract.retained == false && contract.lifetime.kind != NativeLifetimeKind.Call
                || contract.userDataParameter is not StableId userData || contract.callableUserDataIndex is not int dataIndex
                || dataIndex < 0 || dataIndex >= function.parameterTypes.Length)
                throw new NotSupportedException("An aggregate callback requires an explicit context and call-scoped or retained shutdown protocol.");
            NativeDeclaration outerData = module.GetDeclaration(userData);
            if (!callable.children.Contains(userData) || !VoidPointer(outerData.type!.Value) || !VoidPointer(function.parameterTypes[dataIndex]))
                throw new NotSupportedException("A normalized callback requires the same explicit void-pointer context channel in the invocation and callback.");
            if (!function.hasPrototype || function.isVariadic)
                throw new NotSupportedException("A normalized callback must have a fixed native prototype.");
            if (function.parameterTypes.Append(function.returnType!.Value).Any(id => Custom(id) is not null))
                throw new NotSupportedException("A callback involving custom native value conversions requires its explicit callable lowering and reverse conversion protocol.");
            List<StableId> physical = [];
            foreach (StableId id in function.parameterTypes)
            {
                BridgeType storage = Storage(id);
                physical.Add((IsAggregate(id) ? Pointer(storage, true) : storage).id);
            }
            bool resultOut = IsAggregate(function.returnType!.Value);
            BridgeType returnStorage = Storage(resultOut ? module.types.Single(type => type.primitiveKind == "Void").id : function.returnType.Value);
            if (resultOut) physical.Add(Pointer(Storage(Resolve(function.returnType.Value).id), false).id);
            StableId carrierId = new("native-bridge-callback:" + parameter.id.value);
            BridgeType carrier = new(carrierId, BridgeTypeKind.Function, ExportName(callable) + "_callback_" + callbacks.Count)
            { returnType = returnStorage.id, parameterTypes = physical.ToImmutableArray(), callingConvention = "C" };
            Register(carrier);
            callbacks.Add(new(callable.id, parameter.id, function.id, carrier.id, userData, dataIndex, contract));
            return Pointer(carrier, false);
        }
        bool HasAggregate(NativeType function) => function.parameterTypes.Any(IsAggregate)
            || function.returnType is StableId result && IsAggregate(result);
        bool VoidPointer(StableId id) => Resolve(id) is { category: NativeTypeCategory.Pointer, elementType: StableId element }
            && Resolve(element).primitiveKind == "Void";
        bool IsAggregate(StableId id) => Resolve(id).category == NativeTypeCategory.Record;
        NativeType Resolve(StableId id) => StandardLibraryFactReader.Resolve(module, id);
        void Register(BridgeType type)
        {
            if (types.TryGetValue(type.id, out BridgeType? existing) && existing != type)
                throw new InvalidOperationException("A native lowering registered conflicting storage identities.");
            types[type.id] = type;
        }
        void AddFragment(ControlledSourceFragment fragment)
        {
            new ControlledSourceFragmentVerifier().Validate(fragment, module, context.headers, []);
            if (fragment.kind != SourceFragmentKind.TranslationUnit) throw new InvalidOperationException("A native C helper declaration must be a translation-unit fragment.");
            if (fragments.TryGetValue(fragment.digest, out ControlledSourceFragment? previous) && previous != fragment)
                throw new InvalidOperationException("Native helper fragments with the same content identity declare different dependencies or evidence.");
            fragments[fragment.digest] = fragment;
        }
        string ExportName(NativeDeclaration declaration) => context.exportPrefix.TrimEnd('_') + "_call_"
            + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(declaration.id.value)))[..20];
    }
}
