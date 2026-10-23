using System;
using System.Collections.Generic;
using System.Linq;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;

namespace BGCS.Analysis.Bridge;

/// <summary>Checks the complete native construction, callback and cleanup protocol before any managed proxy projection.</summary>
public sealed class VirtualProxyProtocolVerifier
{
    /// <summary>Rejects missing identities, mismatched C signatures and invented ownership or quiescence evidence.</summary>
    /// <param name="module">The independently established C ABI with finite virtual proxy semantics.</param>
    public void Verify(InteropModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (module.virtualProxies.IsDefault || module.virtualProxies.Select(proxy => proxy.id).Distinct().Count() != module.virtualProxies.Length
            || module.virtualProxies.Select(proxy => proxy.name).Distinct(StringComparer.Ordinal).Count() != module.virtualProxies.Length)
            Fail("Virtual proxy protocol identities must be initialized and unique.");
        Dictionary<StableId, InteropFunction> functions = module.functions.ToDictionary(function => function.id);
        HashSet<StableId> channels = [];
        foreach (VirtualProxyContract proxy in module.virtualProxies)
        {
            if (string.IsNullOrWhiteSpace(proxy.name) || string.IsNullOrWhiteSpace(proxy.sourceQualifiedName)
                || proxy.evidence.level is not (EvidenceLevel.Declared or EvidenceLevel.UserAsserted)
                || proxy.slots.IsDefaultOrEmpty || proxy.slots.Select(slot => slot.sourceMethod).Distinct().Count() != proxy.slots.Length
                || !Enum.IsDefined(proxy.lifetime.kind) || !Enum.IsDefined(proxy.quiescence)
                || proxy.lifetime.kind != NativeLifetimeKind.Unknown && proxy.lifetime.evidence.level is EvidenceLevel.Unknown or EvidenceLevel.Inferred
                || proxy.quiescence != NativeQuiescenceKind.Unknown && proxy.lifetime.evidence.level is EvidenceLevel.Unknown or EvidenceLevel.Inferred)
                Fail("A virtual proxy has incomplete source identity, callback slots or lifetime evidence.");
            InteropType concrete = Resolve(proxy.proxyType), nativeInterface = Resolve(proxy.interfaceType);
            if (concrete.category != NativeTypeCategory.Record || concrete.size is not null || concrete.sourceNativeType is not null || concrete.sourceGeneratedType is null
                || nativeInterface.category != NativeTypeCategory.Record || nativeInterface.size is not null || nativeInterface.sourceNativeType != proxy.sourceInterface)
                Fail("A virtual proxy lost its concrete or original opaque native interface identity.");
            InteropFunction factory = Function(proxy.createFunction), destroy = Function(proxy.destroyFunction), cast = Function(proxy.interfaceCastFunction);
            InteropParameter userData = Parameter(factory, proxy.userDataParameter), owned = Output(factory, proxy.proxyResultParameter), borrowed = Output(cast, proxy.interfaceResultParameter);
            if (!VoidPointer(userData.type) || factory.parameters[0].id != userData.id || factory.parameters.Length != proxy.slots.Length + 3
                || !ObjectOutput(owned.type, concrete.id) || owned.ownership != proxy.ownership
                || proxy.ownership.kind != NativeOwnershipKind.Owned || proxy.ownership.releaseFunction != destroy.id || proxy.ownership.allocator is null
                || proxy.ownership.lifetime.kind != NativeLifetimeKind.Owner || proxy.ownership.lifetime.owner != owned.id
                || proxy.ownership.releaseThreadDomain is not (null or "any")
                || proxy.ownership.releaseThreadDomain == "any" && proxy.ownership.evidence.level != EvidenceLevel.UserAsserted
                || proxy.ownership.evidence.level is not (EvidenceLevel.Declared or EvidenceLevel.UserAsserted))
                Fail("A virtual proxy construction lost its exact owned output or matching cleanup domain.");
            InteropParameter cleanup = Parameter(destroy, proxy.destroyInstanceParameter), castInput = Parameter(cast, proxy.castInstanceParameter);
            if (destroy.parameters.Length != 2 || destroy.parameters[0].id != cleanup.id || !ObjectPointer(cleanup.type, concrete.id)
                || destroy.sourceRole != "Destroy" || destroy.sourceOwnerType is not null || destroy.sourceGeneratedOwnerType != concrete.sourceGeneratedType
                || cast.parameters.Length != 3 || cast.parameters[0].id != castInput.id || !ObjectPointer(castInput.type, concrete.id)
                || cast.sourceRole != "BaseCast" || cast.sourceOwnerType is not null || cast.sourceGeneratedOwnerType != concrete.sourceGeneratedType || !ObjectOutput(borrowed.type, nativeInterface.id)
                || borrowed.ownership is not { kind: NativeOwnershipKind.Borrowed, lifetime.kind: NativeLifetimeKind.Owner } borrowing
                || borrowing.lifetime.owner != castInput.id)
                Fail("A virtual proxy changed its concrete destruction or compiler-adjusted borrowed interface relationship.");
            for (int slotIndex = 0; slotIndex < proxy.slots.Length; slotIndex++)
            {
                VirtualProxySlotContract slot = proxy.slots[slotIndex];
                if (!channels.Add(slot.id) || !channels.Add(slot.userDataParameter) || string.IsNullOrWhiteSpace(slot.sourceName)
                    || string.IsNullOrWhiteSpace(slot.sourceSignature) || slot.parameters.IsDefault
                    || factory.parameters[slotIndex + 1].id != slot.factoryParameter || !VoidPointer(Parameter(factory, slot.factoryParameter).type))
                    Fail("A virtual proxy callback lost its unique source method or exact factory argument.");
                InteropType signature = Resolve(slot.callableType);
                if (signature.category != NativeTypeCategory.Function || !signature.hasPrototype || signature.isVariadic
                    || signature.callingConvention is not ("C" or "Default") || signature.returnType is not StableId status
                    || Resolve(status) is not { category: NativeTypeCategory.Primitive, size: 4, isSigned: true }
                    || signature.parameterTypes.Length != slot.parameters.Length + (slot.result is null ? 1 : 2) || !VoidPointer(signature.parameterTypes[0]))
                    Fail("A virtual proxy callback changed its exact fixed Cdecl status and user-data ABI.");
                for (int argument = 0; argument < slot.parameters.Length; argument++)
                {
                    VirtualProxyValueContract value = slot.parameters[argument];
                    if (!channels.Add(value.id) || value.index != argument + 1 || value.type != signature.parameterTypes[value.index]
                        || value.valueType != value.type || value.sourceDeclaration is null || !Enum.IsDefined(value.sourceCategory))
                        Fail("A virtual proxy callback argument lost its exact source and C ABI channel identities.");
                }
                if (slot.result is { } result)
                {
                    InteropType output = Resolve(result.type);
                    if (!channels.Add(result.id) || result.index != signature.parameterTypes.Length - 1 || result.type != signature.parameterTypes[^1]
                        || output.category != NativeTypeCategory.Pointer || output.elementType != result.valueType || result.sourceDeclaration is not null
                        || !Enum.IsDefined(result.sourceCategory))
                        Fail("A virtual proxy callback result lost its exact explicit output pointer.");
                }
            }
        }

        InteropFunction Function(StableId id)
        {
            if (!functions.TryGetValue(id, out InteropFunction? function) || !function.hasPrototype || function.isVariadic || function.callingConvention != "C"
                || Resolve(function.returnType) is not { category: NativeTypeCategory.Primitive, size: 4, isSigned: true }
                || function.error is not { successValue: "0", errorParameter: not null })
                throw new InvalidOperationException("A virtual proxy operation lost its exact status-returning native error boundary.");
            return function;
        }
        static InteropParameter Parameter(
            InteropFunction function,
            StableId id
        ) => function.parameters.SingleOrDefault(parameter => parameter.id == id)
            ?? throw new InvalidOperationException("A virtual proxy references an absent native operation parameter.");
        static InteropParameter Output(
            InteropFunction function,
            StableId id
        ) => function.error?.valueParameter == id ? Parameter(function, id)
            : throw new InvalidOperationException("A virtual proxy changed its native operation's actual result-out identity.");
        bool ObjectOutput(
            StableId id,
            StableId record
        ) => Resolve(id) is { category: NativeTypeCategory.Pointer, elementType: StableId element } && ObjectPointer(element, record);
        bool ObjectPointer(
            StableId id,
            StableId record
        ) => Resolve(id) is { category: NativeTypeCategory.Pointer, elementType: StableId element } && Resolve(element).id == record;
        bool VoidPointer(StableId id) => Resolve(id) is { category: NativeTypeCategory.Pointer, elementType: StableId element }
            && Resolve(element).primitiveKind == "Void";
        InteropType Resolve(StableId id)
        {
            HashSet<StableId> seen = [];
            InteropType type = module.GetType(id);
            while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
            {
                if (!seen.Add(type.id) || type.elementType is not StableId element) throw new InvalidOperationException("A virtual proxy carrier has missing or cyclic alias storage.");
                type = module.GetType(element);
            }
            return type;
        }
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Fail(string message) => throw new InvalidOperationException(message);
}
