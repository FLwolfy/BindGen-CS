using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BGCS.Core.Bridge;
using BGCS.Core.Common;

namespace BGCS.NativeBridge.Planning;

/// <summary>Adds explicit generated proxy operations without inserting generated declarations into the original native graph.</summary>
internal static class VirtualProxyBridgePlanner
{
    internal static NativeBridgePlan Apply(
        NativeBridgePlan bridge,
        IEnumerable<CppVirtualProxyPlan> proxies
    ) {
        Dictionary<StableId, BridgeType> types = bridge.types.ToDictionary(type => type.id);
        List<BridgeExport> exports = bridge.exports.ToList();
        ImmutableArray<CppVirtualProxyPlan>.Builder planned = ImmutableArray.CreateBuilder<CppVirtualProxyPlan>();
        foreach (CppVirtualProxyPlan proxy in proxies)
        {
            BridgeType opaque = Add(new(new("bridge-generated-handle:" + proxy.generatedType.value), BridgeTypeKind.Handle,
                Name(proxy.proxyType + "_object", proxy.generatedType))
            { generatedType = proxy.generatedType });
            BridgeType address = Pointer(opaque, false);
            BridgeType nativeInterface = types.Values.SingleOrDefault(type => type.kind == BridgeTypeKind.Handle && type.nativeType == proxy.nativeInterface)
                ?? Add(new(new("bridge-handle:" + proxy.nativeInterface.value), BridgeTypeKind.Handle,
                    Name(bridge.source.GetType(proxy.nativeInterface).spelling + "_object", proxy.nativeInterface))
                { nativeType = proxy.nativeInterface });
            StableId createId = new("bridge-virtual:" + proxy.id + ":create"), destroyId = new("bridge-virtual:" + proxy.id + ":destroy");
            StableId castId = new("bridge-virtual:" + proxy.id + ":cast"), self = new("bridge-virtual:" + proxy.id + ":self");
            BridgeType voidPointer = Pointer(Scalar("void"), false);
            ImmutableArray<BridgeParameter> parameters = new[] { "userData" }.Concat(proxy.slots.Select(slot => slot.name))
                .Select(name => new BridgeParameter(new("bridge-virtual:" + proxy.id + ":" + name), name, voidPointer.id, null, BridgeConversionKind.PointerCast)).ToImmutableArray();
            exports.Add(new(createId, Name(proxy.factory, createId), parameters,
                new(BridgeOperationKind.VirtualProxyConstruct, null, null, null) { virtualProxyId = proxy.id })
            { resultType = address.id, resultConversion = BridgeConversionKind.OwnedValue, resultRelease = destroyId });
            exports.Add(new(destroyId, Name(proxy.proxyType + "_destroy", destroyId),
                [new(self, "bgcs_self", address.id, null, BridgeConversionKind.PointerCast) { generatedType = proxy.generatedType }],
                new(BridgeOperationKind.VirtualProxyDestroy, null, null, self) { virtualProxyId = proxy.id }));
            exports.Add(new(castId, Name(proxy.proxyType + "_interface", castId),
                [new(self, "bgcs_self", address.id, null, BridgeConversionKind.PointerCast) { generatedType = proxy.generatedType }],
                new(BridgeOperationKind.VirtualProxyCast, null, null, self) { virtualProxyId = proxy.id, castTarget = proxy.nativeInterface })
            { resultType = Pointer(nativeInterface, false).id, nativeResultType = proxy.nativeInterface, resultConversion = BridgeConversionKind.PointerCast });
            planned.Add(proxy with
            {
                slots = proxy.slots.Select(slot =>
            {
                StableId signature = new("bridge-virtual-callback:" + proxy.id + ":" + slot.nativeMethod.value);
                ImmutableArray<StableId> inputs = new[] { voidPointer.id }.Concat(slot.parameters.Select(value => Carrier(value.cType).id))
                    .Concat(slot.result is { } result ? [Pointer(Carrier(result.cType), false).id] : []).ToImmutableArray();
                Add(new(signature, BridgeTypeKind.Function, Name("virtual_callback_" + slot.name, signature))
                { returnType = Scalar("int32_t").id, parameterTypes = inputs, callingConvention = "C" });
                return slot with { bridgeCallableType = signature };
            }).ToImmutableArray()
            });
        }
        if (exports.Select(export => export.name).Distinct(StringComparer.Ordinal).Count() != exports.Count)
            throw new InvalidOperationException("Generated virtual proxy operations conflict with an existing physical export.");
        return bridge with
        {
            types = types.Values.OrderBy(type => type.id.value, StringComparer.Ordinal).ToImmutableArray(),
            exports = exports.OrderBy(export => export.name, StringComparer.Ordinal).ToImmutableArray(),
            virtualProxies = planned.ToImmutable()
        };

        BridgeType Add(BridgeType type)
        {
            if (types.TryGetValue(type.id, out BridgeType? existing)) return existing;
            types.Add(type.id, type); return type;
        }
        BridgeType Scalar(string name) => Add(new(new("bridge-scalar:" + name), BridgeTypeKind.Scalar, name));
        BridgeType Pointer(
            BridgeType type,
            bool constant
        ) => Add(new(new("bridge-pointer:" + (constant ? "const:" : "") + type.id.value), BridgeTypeKind.Pointer, string.Empty)
        { elementType = type.id, isConst = constant });
        BridgeType Carrier(string spelling) => spelling switch
        {
            "void*" => Pointer(Scalar("void"), false),
            "const void*" => Pointer(Scalar("void"), true),
            _ => Scalar(spelling)
        };
        string Name(
            string hint,
            StableId identity
        ) {
            string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(hint + "|" + identity.value)))[..16];
            string normalized = new(hint.Select(character => char.IsAsciiLetterOrDigit(character) ? character : '_').ToArray());
            while (normalized.Contains("__", StringComparison.Ordinal)) normalized = normalized.Replace("__", "_", StringComparison.Ordinal);
            normalized = normalized.Trim('_');
            if (normalized.Length > 80) normalized = normalized[..80].TrimEnd('_');
            return bridge.exportPrefix.TrimEnd('_') + "_" + normalized + "_" + hash;
        }
    }
}
