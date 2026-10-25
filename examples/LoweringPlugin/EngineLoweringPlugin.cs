using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BGCS.Core.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Native;
using BGCS.Core.Plugins;
using BGCS.Core.Ports;
using BGCS.Core.Requests;

namespace BGCS.Examples.LoweringPlugin;

/// <summary>Plans one complete typed member export using only neutral Contracts.</summary>
public sealed class EngineLoweringPlugin : IBindingPlugin, ICacheFingerprintProvider
{
    /// <inheritdoc />
    public PluginDescriptor descriptor => new("bgcs.examples.engine-lowering", "4.1.2",
        [new("engine.rename-tick", ExtensionPort.NativeCallableLowering, 100)]);

    /// <inheritdoc />
    public void Configure(
        IExtensionRegistryBuilder registry,
        PluginConfiguration configuration
    )
        => registry.RegisterFactory<INativeCallableLowering>("engine.rename-tick", _ => new RenameTickLowering());

    /// <inheritdoc />
    public CacheFingerprintContribution CaptureFingerprint(
        CacheFingerprintContext context,
        CancellationToken cancellationToken
    )
        => Fingerprint("engine-lowering-plugin:4.1.2", cancellationToken);

    private static CacheFingerprintContribution Fingerprint(
        string state,
        CancellationToken cancellationToken
    ) {
        cancellationToken.ThrowIfCancellationRequested();
        return new(new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(state)))), []);
    }

    private sealed class RenameTickLowering : INativeCallableLowering, ICacheFingerprintProvider
    {
        /// <inheritdoc />
        public string id => "engine.rename-tick";
        /// <inheritdoc />
        public int priority => 100;

        /// <inheritdoc />
        public bool CanLower(
            NativeDeclaration callable,
            LoweringContext context
        )
            => callable.kind == "Function" && callable.qualifiedName == "Engine::World::Tick"
                && !callable.isStatic && !callable.isVariadic && callable.hasPrototype
                && callable.parent is StableId parent && context.module.GetDeclaration(parent).type is not null
                && callable.returnType is StableId result && context.module.GetType(result).primitiveKind == "Void"
                && !callable.children.Select(context.module.GetDeclaration).Any(child => child.kind == "Parameter");

        /// <inheritdoc />
        public NativeCallableLoweringPlan Lower(
            NativeDeclaration callable,
            LoweringContext context,
            CancellationToken cancellationToken
        ) {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CanLower(callable, context)) throw new ArgumentException("The extension supports only void World.Tick() with no parameters.", nameof(callable));
            NativeDeclaration owner = context.module.GetDeclaration(callable.parent!.Value);
            StableId nativeType = owner.type!.Value;
            StableId handleId = new("engine-handle:" + nativeType.value);
            StableId pointerId = new("engine-pointer:" + (callable.isConst ? "const:" : string.Empty) + nativeType.value);
            StableId selfId = new("engine-self:" + callable.id.value);
            BridgeType handle = new(handleId, BridgeTypeKind.Handle, "engine_world_handle") { nativeType = nativeType };
            BridgeType pointer = new(pointerId, BridgeTypeKind.Pointer, string.Empty) { elementType = handleId, isConst = callable.isConst };
            BridgeParameter self = new(selfId, "bgcs_self", pointerId, nativeType, BridgeConversionKind.PointerCast);
            BridgeExport export = new(new("engine-tick:" + callable.id.value), "engine_world_tick", [self],
                new(BridgeOperationKind.InstanceCall, callable.id, nativeType, selfId))
            { documentation = callable.documentation };
            return new(export, [handle, pointer], []);
        }

        /// <inheritdoc />
        public CacheFingerprintContribution CaptureFingerprint(
            CacheFingerprintContext context,
            CancellationToken cancellationToken
        )
            => Fingerprint("engine-world-tick:void/no-parameters:4.1.2", cancellationToken);
    }
}
