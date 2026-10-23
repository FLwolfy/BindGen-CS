using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using BGCS.Analysis.Types;
using BGCS.Core.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;

namespace BGCS.Analysis.Bridge;

/// <summary>Checks native virtual adapter metadata and associates it with independently reparsed final C callable identities.</summary>
public sealed class VirtualProxyProtocolBuilder
{
    /// <summary>Rejects invented proxy, callback, construction or native adjustment relationships before source emission.</summary>
    /// <param name="bridge">The typed operation graph and its independently parsed native adapter facts.</param>
    public void VerifyBridge(NativeBridgePlan bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        if (bridge.virtualProxies.IsDefault || bridge.virtualProxies.Select(proxy => proxy.id).Distinct(StringComparer.Ordinal).Count() != bridge.virtualProxies.Length)
            throw new InvalidOperationException("Virtual proxy protocols need initialized unique stable identities.");
        Dictionary<StableId, BridgeType> types = bridge.types.ToDictionary(type => type.id);
        foreach (CppVirtualProxyPlan proxy in bridge.virtualProxies)
        {
            if (proxy.releaseThreadDomain is not ("unknown" or "any")
                || proxy.releaseThreadDomain == "any" && proxy.cleanupEvidence.level != EvidenceLevel.UserAsserted
                || proxy.releaseThreadDomain == "unknown" && proxy.cleanupEvidence.level != EvidenceLevel.Unknown)
                Fail("A virtual proxy cleanup thread claim must preserve its explicit assertion or unknown evidence.");
            new ControlledSourceFragmentVerifier().Validate(proxy.source, bridge.source, bridge.headers, []);
            NativeType sourceInterface = Source(proxy.nativeInterface);
            if (sourceInterface.category != NativeTypeCategory.Record || sourceInterface.declaration is not StableId ownerId
                || bridge.source.GetDeclaration(ownerId) is not { isAbstract: true, isCompleteDefinition: true })
                Fail("The original virtual interface lost its complete abstract native identity.");
            BridgeType concrete = bridge.types.Single(type => type.kind == BridgeTypeKind.Handle && type.generatedType == proxy.generatedType);
            if (concrete.nativeType is not null || bridge.source.types.Any(type => type.id == proxy.generatedType))
                Fail("A generated proxy identity must remain separate from original native facts.");
            BridgeExport factory = Factory(bridge, proxy);
            if (factory.nativeResultType is not null || factory.resultConversion != BridgeConversionKind.OwnedValue
                || factory.resultRelease is not StableId releaseId || factory.parameters.Length != proxy.slots.Length + 1
                || factory.resultType is not StableId resultType || !GeneratedPointer(resultType))
                Fail("The virtual proxy factory lost its exact generated owned construction or callback inputs.");
            BridgeExport destroy = bridge.exports.Single(export => export.id == factory.resultRelease);
            if (destroy.operation.kind != BridgeOperationKind.VirtualProxyDestroy || destroy.operation.virtualProxyId != proxy.id
                || destroy.parameters.Length != 1 || destroy.resultType is not null || destroy.nativeResultType is not null)
                Fail("The virtual proxy factory no longer uses its own exact generated destruction domain.");
            BridgeExport cast = Cast(bridge, proxy);
            if (cast.parameters.Length != 1 || cast.resultType is not StableId castResult || cast.nativeResultType != proxy.nativeInterface
                || cast.resultConversion != BridgeConversionKind.PointerCast || cast.resultRelease is not null
                || !NativeInterfacePointer(castResult)) Fail("The native interface cast lost its exact input and output channels.");
            foreach (BridgeExport operation in new[] { factory, destroy, cast })
                if (operation.operation.declaration is not null || operation.operation.ownerType is not null
                    || operation.operation.finiteVariadic is not null || operation.operation.standardValue is not null
                    || !operation.operation.argumentGroups.IsEmpty || operation.operation.invocationExpression is not null
                    || operation.resultExpression is not null || operation.resultOwnership is not null || operation.inputAdapterOf is not null
                    || operation.resultBorrowsInstance || !operation.resultInOutputParameter || !operation.usesErrorChannel
                    || operation.parameters.Any(parameter => parameter.nativeType is not null || parameter.conversion != BridgeConversionKind.PointerCast
                        || parameter.conversionExpression is not null || parameter.managedProjection is not null))
                    Fail("A generated proxy operation carries unrelated original declarations or conversion protocols.");
            if (factory.operation.instanceParameter is not null || factory.operation.castTarget is not null
                || destroy.operation.castTarget is not null || cast.operation.castTarget != proxy.nativeInterface
                || destroy.operation.instanceParameter != destroy.parameters[0].id || cast.operation.instanceParameter != cast.parameters[0].id
                || !GeneratedPointer(destroy.parameters[0].type) || !GeneratedPointer(cast.parameters[0].type)
                || destroy.parameters[0].generatedType != proxy.generatedType || cast.parameters[0].generatedType != proxy.generatedType)
                Fail("Generated proxy cleanup and adjustment must use their exact nonconst generated owner.");
            if (factory.parameters[0].name != "userData" || factory.parameters.Any(parameter => !IsVoidPointer(types[parameter.type]) || parameter.generatedType is not null))
                Fail("The virtual proxy factory changed its explicit borrowed address channels.");
            bool GeneratedPointer(StableId id) => types[id] is { kind: BridgeTypeKind.Pointer, isConst: false, elementType: StableId element }
                && element == concrete.id;
            bool NativeInterfacePointer(StableId id) => types[id] is { kind: BridgeTypeKind.Pointer, isConst: false, elementType: StableId element }
                && types[element] is { kind: BridgeTypeKind.Handle } carrier && carrier.nativeType == proxy.nativeInterface && carrier.generatedType is null;
            if (proxy.slots.IsEmpty || proxy.slots.Select(slot => slot.nativeMethod).Distinct().Count() != proxy.slots.Length
                || proxy.slots.Select(slot => slot.name).Distinct(StringComparer.Ordinal).Count() != proxy.slots.Length)
                Fail("The virtual proxy has missing or duplicate source virtual methods.");
            for (int index = 0; index < proxy.slots.Length; index++)
            {
                VirtualCallbackSlot slot = proxy.slots[index];
                NativeDeclaration method = bridge.source.GetDeclaration(slot.nativeMethod);
                if (method.kind != "Function" || !method.isVirtual || !method.isAbstract || method.isNoThrow != false
                    || !method.hasPrototype || method.isVariadic || !Enum.IsDefined(method.referenceQualifier) || method.referenceQualifier == NativeReferenceQualifier.Unknown
                    || slot.name != "slot" + index.ToString(CultureInfo.InvariantCulture) || factory.parameters[index + 1].name != slot.name)
                    Fail("A virtual callback slot lost its exact source method, exception or factory-position contract.");
                if (slot.bridgeCallableType is not StableId callbackId || !types.TryGetValue(callbackId, out BridgeType? callback)
                    || callback.kind != BridgeTypeKind.Function || callback.nativeType is not null
                    || callback.isVariadic || !callback.hasPrototype || callback.callingConvention is not ("C" or "Default")
                    || callback.returnType is not StableId status || types[status] is not { kind: BridgeTypeKind.Scalar, cName: "int32_t" }
                    || callback.parameterTypes.Length != slot.parameters.Length + (slot.result is null ? 1 : 2)
                    || !IsVoidPointer(types[callback.parameterTypes[0]]))
                    Fail("A virtual callback field lost its exact fixed status-returning Cdecl signature.");
                BridgeType signature = types[slot.bridgeCallableType!.Value];
                NativeDeclaration[] inputs = method.children.Select(bridge.source.GetDeclaration).Where(declaration => declaration.kind == "Parameter").ToArray();
                if (inputs.Length != slot.parameters.Length) Fail("A virtual callback changed its original native parameter count.");
                for (int argument = 0; argument < inputs.Length; argument++)
                    if (inputs[argument].type != slot.parameters[argument].nativeType
                        || !ValueMatches(slot.parameters[argument], types[signature.parameterTypes[argument + 1]], false))
                        Fail("A virtual callback argument no longer has its declared normalized C value channel.");
                bool hasResult = method.returnType is StableId result && Source(result).primitiveKind != "Void";
                if (hasResult != (slot.result is not null)) Fail("A virtual callback changed its original result presence.");
                if (slot.result is { } output)
                {
                    BridgeType channel = types[signature.parameterTypes[^1]];
                    if (output.nativeType != method.returnType || channel.kind != BridgeTypeKind.Pointer || channel.isConst || channel.elementType is not StableId value
                        || !ValueMatches(output, types[value], true))
                        Fail("A virtual callback result lost its explicit normalized output storage.");
                }
            }
        }

        NativeType Source(StableId id) => StandardLibraryFactReader.Resolve(bridge.source, id);
        bool IsVoidPointer(BridgeType type) => type.kind == BridgeTypeKind.Pointer && !type.isConst
            && type.elementType is StableId element && types[element] is { kind: BridgeTypeKind.Scalar, cName: "void" };
        bool IsConst(StableId id)
        {
            NativeType type = bridge.source.GetType(id);
            return type.category == NativeTypeCategory.Qualified && type.qualifier?.Contains("Const", StringComparison.Ordinal) == true
                || type.category is NativeTypeCategory.Qualified or NativeTypeCategory.Alias && type.elementType is StableId element && IsConst(element);
        }
        bool ValueMatches(
            VirtualCallbackValue value,
            BridgeType carrier,
            bool isResult
        ) {
            NativeType original = Source(value.nativeType);
            if (original.category == NativeTypeCategory.Pointer || !isResult && original.category is NativeTypeCategory.Record or NativeTypeCategory.Reference)
            {
                bool constant = original.category == NativeTypeCategory.Record || IsConst(original.elementType!.Value);
                return carrier.kind == BridgeTypeKind.Pointer && carrier.isConst == constant && carrier.elementType is StableId element
                    && types[element] is { kind: BridgeTypeKind.Scalar, cName: "void" } && value.cType == (constant ? "const void*" : "void*");
            }
            NativeType scalar = original.category == NativeTypeCategory.Enumeration ? Source(original.elementType!.Value) : original;
            string? name = scalar.primitiveKind switch
            {
                "Bool" when scalar.size == 1 => "uint8_t",
                "Float" when scalar.size == 4 => "float",
                "Double" when scalar.size == 8 => "double",
                _ when scalar.category == NativeTypeCategory.Primitive && scalar.size is 1 or 2 or 4 or 8 && scalar.isSigned is bool signed
                    => (signed ? "int" : "uint") + (scalar.size.Value * 8).ToString(CultureInfo.InvariantCulture) + "_t",
                _ => null
            };
            return name is not null && carrier.kind == BridgeTypeKind.Scalar && carrier.cName == name && value.cType == name;
        }
    }

    /// <summary>Associates verified proxy operations and typed callback typedefs with final interoperable IDs.</summary>
    /// <param name="bridge">The checked native bridge.</param>
    /// <param name="module">Its independently reparsed C ABI with restored ownership.</param>
    /// <returns>The same native ABI enriched with finite virtual protocols and unknown caller retention.</returns>
    public InteropModule Attach(
        NativeBridgePlan bridge,
        InteropModule module
    ) {
        VerifyBridge(bridge);
        ArgumentNullException.ThrowIfNull(module);
        if (bridge.source.targetId != module.targetId || bridge.source.pointerSize != module.pointerSize || bridge.source.byteOrder != module.byteOrder)
            throw new InvalidOperationException("A virtual proxy protocol cannot cross native target identities.");
        Dictionary<StableId, InteropFunction> functions = module.functions.ToDictionary(function => function.id);
        Dictionary<StableId, BridgeType> bridgeTypes = bridge.types.ToDictionary(type => type.id);
        ImmutableArray<VirtualProxyContract> protocols = bridge.virtualProxies.Select(proxy =>
        {
            BridgeExport plannedFactory = Factory(bridge, proxy);
            InteropFunction factory = functions[plannedFactory.id];
            InteropFunction destroy = functions[plannedFactory.resultRelease!.Value];
            InteropFunction cast = functions[Cast(bridge, proxy).id];
            InteropParameter owned = factory.parameters.Single(parameter => parameter.id == factory.error!.valueParameter);
            OwnershipContract ownership = owned.ownership ?? throw new InvalidOperationException("A virtual proxy lost its owned factory result contract.");
            if (ownership.kind != NativeOwnershipKind.Owned || ownership.releaseFunction != destroy.id || ownership.allocator is null)
                Fail("A virtual proxy lost its matching allocation and cleanup domain.");
            if (proxy.releaseThreadDomain == "any" && (ownership.releaseThreadDomain != "any" || ownership.evidence != proxy.cleanupEvidence)
                || proxy.releaseThreadDomain == "unknown" && ownership.releaseThreadDomain is not null)
                Fail("A virtual proxy lost its exact caller-asserted or unknown cleanup thread evidence.");
            InteropParameter borrowed = cast.parameters.Single(parameter => parameter.id == cast.error!.valueParameter);
            InteropType concrete = OpaqueResult(owned), nativeInterface = OpaqueResult(borrowed);
            if (concrete.sourceNativeType is not null || concrete.sourceGeneratedType != proxy.generatedType || nativeInterface.sourceNativeType != proxy.nativeInterface
                || borrowed.ownership is not { kind: NativeOwnershipKind.Borrowed, lifetime.kind: NativeLifetimeKind.Owner } borrowing
                || borrowing.lifetime.owner != cast.parameters[0].id)
                Fail("A virtual proxy lost its original concrete identity or native-adjusted borrowed interface owner.");
            NativeDeclaration original = bridge.source.GetDeclaration(bridge.source.GetType(proxy.nativeInterface).declaration!.Value);
            Evidence evidence = new(EvidenceLevel.Declared, "native-virtual-proxy:" + proxy.id);
            ImmutableArray<VirtualProxySlotContract> slots = proxy.slots.Select((
                slot,
                slotIndex
            ) => {
                NativeDeclaration method = bridge.source.GetDeclaration(slot.nativeMethod);
                BridgeType plannedCallback = bridgeTypes[slot.bridgeCallableType!.Value];
                InteropType alias = module.types.Single(type => type.category == NativeTypeCategory.Alias && type.declarationName == plannedCallback.cName);
                InteropType callback = Abi(alias.id);
                if (callback.category != NativeTypeCategory.Function || callback.parameterTypes.Length != slot.parameters.Length + (slot.result is null ? 1 : 2))
                    Fail("A virtual proxy callback lost its independently reparsed callable type.");
                StableId slotId = new("virtual-slot:" + proxy.id + ":" + slot.nativeMethod.value);
                NativeDeclaration[] inputs = method.children.Select(bridge.source.GetDeclaration).Where(declaration => declaration.kind == "Parameter").ToArray();
                ImmutableArray<VirtualProxyValueContract> arguments = slot.parameters.Select((
                    value,
                    index
                ) => Channel(
                    new(slotId.value + ":argument:" + index.ToString(CultureInfo.InvariantCulture)), index + 1, callback.parameterTypes[index + 1],
                    callback.parameterTypes[index + 1], value.nativeType, inputs[index].name, inputs[index])).ToImmutableArray();
                VirtualProxyValueContract? result = slot.result is { } output
                    ? Channel(new(slotId.value + ":result"), callback.parameterTypes.Length - 1, callback.parameterTypes[^1], Abi(callback.parameterTypes[^1]).elementType!.Value,
                        output.nativeType, "result", null) : null;
                return new VirtualProxySlotContract(slotId, method.id, method.name, callback.id, factory.parameters[slotIndex + 1].id,
                    new(slotId.value + ":user-data"), arguments, result)
                { source = method.source, sourceSignature = new NativeTypeFormatter().CallableSignature(bridge.source, method) };

                VirtualProxyValueContract Channel(
                    StableId id,
                    int index,
                    StableId type,
                    StableId valueType,
                    StableId sourceType,
                    string name,
                    NativeDeclaration? declaration
                ) {
                    NativeType originalType = StandardLibraryFactReader.Resolve(bridge.source, sourceType);
                    return new(id, index, type, valueType, sourceType, name)
                    {
                        sourceDeclaration = declaration?.id,
                        source = declaration?.source ?? method.source,
                        sourceCategory = originalType.category,
                        sourcePrimitiveKind = originalType.primitiveKind
                    };
                }
            }).ToImmutableArray();
            return new VirtualProxyContract(new("virtual-proxy:" + module.libraryId + ":" + proxy.id), proxy.id, proxy.nativeInterface,
                concrete.id, nativeInterface.id, factory.id, destroy.id, cast.id, factory.parameters[0].id, owned.id,
                destroy.parameters[0].id, cast.parameters[0].id, borrowed.id, ownership, slots, evidence)
            { source = original.source, sourceQualifiedName = original.qualifiedName };
        }).ToImmutableArray();
        InteropModule enriched = new(module.libraryId, module.targetId, module.pointerSize, module.types, module.functions, module.diagnostics)
        { byteOrder = module.byteOrder, constants = module.constants, virtualProxies = protocols, functionAliases = module.functionAliases, stringConstants = module.stringConstants };
        new VirtualProxyProtocolVerifier().Verify(enriched);
        return enriched;

        InteropType Abi(StableId id)
        {
            HashSet<StableId> visited = [];
            InteropType type = module.GetType(id);
            while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
            {
                if (!visited.Add(type.id) || type.elementType is not StableId element) throw new InvalidOperationException("A virtual proxy C carrier has a missing or cyclic alias.");
                type = module.GetType(element);
            }
            return type;
        }
        InteropType OpaqueResult(InteropParameter output)
        {
            InteropType address = Abi(output.type);
            if (address.category != NativeTypeCategory.Pointer || address.elementType is not StableId value) throw new InvalidOperationException("A virtual proxy result lost its output pointer.");
            InteropType pointer = Abi(value);
            if (pointer.category != NativeTypeCategory.Pointer || pointer.elementType is not StableId opaque) throw new InvalidOperationException("A virtual proxy result lost its object pointer.");
            InteropType result = Abi(opaque);
            if (result.category != NativeTypeCategory.Record || result.size is not null) throw new InvalidOperationException("A virtual proxy lost its opaque object carrier.");
            return result;
        }
    }

    private static BridgeExport Factory(
        NativeBridgePlan bridge,
        CppVirtualProxyPlan proxy
    )
        => bridge.exports.Single(export => export.operation.kind == BridgeOperationKind.VirtualProxyConstruct && export.operation.virtualProxyId == proxy.id);
    private static BridgeExport Cast(
        NativeBridgePlan bridge,
        CppVirtualProxyPlan proxy
    )
        => bridge.exports.Single(export => export.operation.kind == BridgeOperationKind.VirtualProxyCast && export.operation.virtualProxyId == proxy.id);
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Fail(string message) => throw new InvalidOperationException(message);
}
