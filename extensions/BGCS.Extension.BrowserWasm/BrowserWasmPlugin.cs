using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BGCS.Core;
using BGCS.Core.Calls;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using BGCS.Core.Plugins;
using BGCS.Core.Ports;
using BGCS.Core.Requests;
using BGCS.Core.Targets;

namespace BGCS.Extension.BrowserWasm;

/// <summary>Resolves the explicitly selected Mono browser wasm32 execution mechanism.</summary>
public sealed class BrowserWasmRuntimeProvider : IManagedRuntimeProvider, ICacheFingerprintProvider
{
    /// <inheritdoc />
    public CacheFingerprintContribution CaptureFingerprint(
        CacheFingerprintContext context,
        CancellationToken cancellationToken
    )
        => BrowserWasmFingerprint.Capture(GetType(), cancellationToken);

    /// <inheritdoc />
    public bool TryResolve(
        ManagedRuntimeRequest request,
        [NotNullWhen(true)] out ManagedRuntimeProfile? profile
    ) {
        ArgumentNullException.ThrowIfNull(request);
        profile = null;
        if (request.runtimeId.value != "dotnet.mono-wasm") return false;
        NativeTargetDescriptor target = request.target;
        if (target.platformId != "emscripten" || target.architectureId != "wasm32" || target.abiId != "emscripten"
            || target.triple != "wasm32-unknown-emscripten" || target.byteOrder != NativeByteOrder.LittleEndian
            || target.capabilities is not { supportsDynamicModules: false })
            throw new NotSupportedException("The browser execution provider requires exact Emscripten wasm32 facts and static native-module capabilities.");
        // Trimmed Mono interpreter calli support depends on generated P/Invoke cookies.
        // A function address alone cannot establish that execution contract, including
        // on an application that may later choose AOT. Admit the deterministic imports.
        profile = new(target.targetId.value + "/dotnet.mono-wasm/v2", request.runtimeId, NativeModuleMechanism.StaticAdapter,
            [BindingImportMode.DllImport, BindingImportMode.LibraryImport], true, false)
        {
            nativeTargetId = target.targetId.value,
            nativeTriple = target.triple,
            pointerSize = 4,
            byteOrder = NativeByteOrder.LittleEndian,
            callAbi = new("mono-wasm.pointer-boundary"),
            functionPointerTransport = ManagedFunctionPointerTransport.NativeInteger,
            libraryImportConvention = ManagedImportConventionStrategy.RuntimeDefault,
            defaultCallingConvention = "Cdecl"
        };
        return true;
    }
}

/// <summary>Establishes exact wasm32 scalar and address channels; aggregates require generated pointer adapters.</summary>
public sealed class BrowserWasmCallAbiProvider : ICallAbiProvider, ICacheFingerprintProvider
{
    /// <inheritdoc />
    public CacheFingerprintContribution CaptureFingerprint(
        CacheFingerprintContext context,
        CancellationToken cancellationToken
    )
        => BrowserWasmFingerprint.Capture(GetType(), cancellationToken);

    /// <inheritdoc />
    public CallAbiAssessment Assess(CallAbiRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ManagedRuntimeProfile runtime = request.runtime;
        if (runtime.runtimeId.value != "dotnet.mono-wasm")
            return new(runtime.callAbi, NativeCallDisposition.NotApplicable, "The requested runtime belongs to another extension.");
        try
        {
            if (!new BrowserWasmRuntimeProvider().TryResolve(new(runtime.runtimeId, request.target), out ManagedRuntimeProfile? expected)
                || ManagedRuntimeProfileIdentity.Capture(expected) != ManagedRuntimeProfileIdentity.Capture(runtime)
                || runtime.nativeTargetId != request.module.targetId || request.module.pointerSize != 4
                || request.module.byteOrder != NativeByteOrder.LittleEndian)
                return Reject("The exact browser runtime, target and native storage facts do not match.");
            NativeCallSignature signature = request.signature;
            if (!Enum.IsDefined(signature.direction) || signature.isVariadic || !signature.hasPrototype || signature.parameterTypes.IsDefault
                || signature.callingConvention is not ("C" or "Cdecl"))
                return Reject("The browser pointer boundary requires an exact fixed C prototype.");
            NativeCallValuePlan? result = Value(signature.returnType, allowVoid: true);
            ImmutableArray<NativeCallValuePlan> parameters = signature.parameterTypes.Select(id => Value(id, allowVoid: false)!).ToImmutableArray();
            return new(runtime.callAbi, NativeCallDisposition.Direct, "The full wasm32 scalar/address signature uses the implemented fixed C boundary.",
                new(signature, request.module.targetId, runtime.runtimeId, runtime.id, runtime.callAbi, 4, NativeByteOrder.LittleEndian, result, parameters)
                { nativeTriple = request.target.triple, runtimeProfileDigest = ManagedRuntimeProfileIdentity.Capture(runtime) });
        }
        catch (NotSupportedException failure) { return new(runtime.callAbi, NativeCallDisposition.RequiresBridge, failure.Message); }

        CallAbiAssessment Reject(string reason) => new(runtime.callAbi, NativeCallDisposition.Unsupported, reason);
        NativeCallValuePlan? Value(
            StableId id,
            bool allowVoid
        ) {
            HashSet<StableId> seen = [];
            InteropType type = request.module.GetType(id);
            while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
            {
                if (!seen.Add(type.id) || type.elementType is not StableId next) throw new NotSupportedException("A wasm32 carrier contains an incomplete or cyclic alias.");
                type = request.module.GetType(next);
            }
            if (type.category == NativeTypeCategory.Primitive && type.primitiveKind == "Void")
            {
                if (allowVoid) return null;
                throw new NotSupportedException("Void cannot be a parameter carrier.");
            }
            if (type.category is NativeTypeCategory.Record or NativeTypeCategory.Array)
                throw new NotSupportedException("Aggregate values require native pointer and output adapters in the browser execution model.");
            if (type.category == NativeTypeCategory.Enumeration && type.elementType is StableId integral)
            {
                NativeCallValuePlan value = Value(integral, allowVoid: false)!;
                if (type.size != value.size || type.alignment != value.alignment) throw new NotSupportedException("The enum carrier has mismatched native storage.");
                return value with { nativeType = id };
            }
            if (type.category is NativeTypeCategory.Pointer or NativeTypeCategory.Function)
            {
                if (type.size != 4 || type.alignment != 4) throw new NotSupportedException("The address carrier must match wasm32 pointer storage.");
                return new(id, 4, 4, ["Address32"]);
            }
            if (type.size is not > 0 || type.alignment is not > 0 || type.category != NativeTypeCategory.Primitive
                || !(type.primitiveKind == "Float" && type.size == 4 || type.primitiveKind == "Double" && type.size == 8
                    || type.primitiveKind == "Bool" && type.size == 1 || type.isSigned is not null && type.size is 1 or 2 or 4 or 8))
                throw new NotSupportedException("This native value has no implemented wasm32 scalar channel; use an explicit pointer or semantic shim.");
            return new(id, type.size.Value, type.alignment.Value, [type.primitiveKind is "Float" or "Double" ? "WasmFloat" : "WasmInteger"]);
        }
    }
}

/// <summary>Registers optional browser target, runtime and scalar calling mechanisms without an engine dependency.</summary>
public sealed class BrowserWasmPlugin : IBindingPlugin, ICacheFingerprintProvider
{
    /// <inheritdoc />
    public PluginDescriptor descriptor => new("bgcs.extension.browser-wasm", "4.1.2", [
        new("browser-wasm.target", ExtensionPort.NativeTargetProvider), new("browser-wasm.execution", ExtensionPort.ManagedRuntimeProvider),
        new("browser-wasm.call-abi", ExtensionPort.CallAbiProvider)]);
    /// <inheritdoc />
    public CacheFingerprintContribution CaptureFingerprint(
        CacheFingerprintContext context,
        CancellationToken cancellationToken
    )
        => BrowserWasmFingerprint.Capture(GetType(), cancellationToken);
    /// <inheritdoc />
    public void Configure(
        IExtensionRegistryBuilder registry,
        PluginConfiguration configuration
    ) {
        registry.RegisterFactory<INativeTargetProvider>("browser-wasm.target", static _ => new BrowserWasmTargetProvider());
        registry.RegisterFactory<IManagedRuntimeProvider>("browser-wasm.execution", static _ => new BrowserWasmRuntimeProvider());
        registry.RegisterFactory<ICallAbiProvider>("browser-wasm.call-abi", static _ => new BrowserWasmCallAbiProvider());
    }
}

internal static class BrowserWasmFingerprint
{
    internal static CacheFingerprintContribution Capture(
        Type service,
        CancellationToken cancellationToken
    ) {
        cancellationToken.ThrowIfCancellationRequested();
        string state = service.FullName + "/v1/" + service.Assembly.ManifestModule.ModuleVersionId.ToString("D");
        return new(new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(state)))), []);
    }
}
