using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Core.Calls;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using BGCS.Core.Ports;
using BGCS.Core.Targets;
using BGCS.Core.Plugins;
using System.Threading;

namespace BGCS.Interop.DotNet;

/// <summary>Implements common .NET carrier classification outside the platform-neutral model and emitter.</summary>
public static class DotNetCallAbi
{
    /// <summary>Assesses an exact signature against the execution model established by its owning runtime provider.</summary>
    /// <param name="request">The final native facts and requested physical signature.</param>
    /// <param name="expectedRuntime">The independently resolved profile from the owning runtime extension.</param>
    /// <returns>A complete direct plan or an explicit rejection/bridge requirement.</returns>
    public static CallAbiAssessment Assess(
        CallAbiRequest request,
        ManagedRuntimeProfile expectedRuntime
    ) => AssessCore(request, expectedRuntime, null);

    internal static CallAbiAssessment AssessDerivedProfile(
        CallAbiRequest request,
        ManagedRuntimeProfile expectedRuntime,
        ContentDigest profileDigest
    ) => AssessCore(request, expectedRuntime, profileDigest);

    private static CallAbiAssessment AssessCore(
        CallAbiRequest request,
        ManagedRuntimeProfile expectedRuntime,
        ContentDigest? profileDigest
    ) {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(expectedRuntime);
        ManagedRuntimeProfile runtime = request.runtime;
        NativeTargetDescriptor target = request.target;
        InteropModule module = request.module;
        CallAbiId provider = runtime.callAbi;
        if (provider.value is not ("dotnet.windows-x64" or "dotnet.system-v-amd64" or "dotnet.pointer-boundary"))
            return new(provider, NativeCallDisposition.NotApplicable, "The requested calling algorithm belongs to another extension.");
        if (runtime.nativeTargetId != module.targetId || target.targetId.value != module.targetId
            || runtime.nativeTriple != target.triple || runtime.pointerSize != module.pointerSize
            || runtime.byteOrder != module.byteOrder || target.byteOrder != module.byteOrder
            || runtime.runtimeId != expectedRuntime.runtimeId || runtime.id != expectedRuntime.id || runtime.pointerSize != expectedRuntime.pointerSize
            || runtime.byteOrder != NativeByteOrder.LittleEndian || runtime.callAbi != expectedRuntime.callAbi
            || runtime.functionPointerTransport != expectedRuntime.functionPointerTransport
            || runtime.libraryImportConvention != expectedRuntime.libraryImportConvention
            || runtime.defaultCallingConvention != expectedRuntime.defaultCallingConvention
            || runtime.requiresStaticCallbackThunks != expectedRuntime.requiresStaticCallbackThunks
            || runtime.allowsBlockingDisposal != expectedRuntime.allowsBlockingDisposal
            || runtime.moduleMechanism != expectedRuntime.moduleMechanism && runtime.moduleMechanism != NativeModuleMechanism.ExternalFunctionTable
            || runtime.importModes.IsDefaultOrEmpty || runtime.importModes.Any(mode => !expectedRuntime.importModes.Contains(mode)))
            return Reject("The runtime, native target and established physical module do not match.");
        if (provider.value == "dotnet.windows-x64" && (target.platformId != "windows" || target.architectureId != "x64"
            || target.abiId != "msvc" || !target.triple.StartsWith("x86_64-", StringComparison.Ordinal)
            || !target.triple.Contains("windows-msvc", StringComparison.Ordinal) || module.pointerSize != 8)
            || provider.value == "dotnet.system-v-amd64" && (target.architectureId != "x64" || module.pointerSize != 8
                || !target.triple.StartsWith("x86_64-", StringComparison.Ordinal)
                || !(target.platformId == "linux" && target.abiId == "gnu" && target.triple.Contains("linux-gnu", StringComparison.Ordinal)
                    || target.platformId == "macos" && target.abiId == "darwin" && (target.triple.Contains("darwin", StringComparison.Ordinal) || target.triple.Contains("macos", StringComparison.Ordinal)))))
            return Reject("The selected calling algorithm is not implemented for these target facts.");
        if (!Enum.IsDefined(request.signature.direction) || request.signature.isVariadic || !request.signature.hasPrototype || request.signature.parameterTypes.IsDefault)
            return Reject("A direct .NET import requires an exact fixed prototype; generate a finite native adapter first.");
        string? convention = request.signature.callingConvention switch
        {
            "C" or "Cdecl" => "Cdecl",
            "X86StdCall" or "StdCall" or "Stdcall" => "Stdcall",
            "X86FastCall" or "FastCall" or "Fastcall" => "Fastcall",
            "X86ThisCall" or "ThisCall" or "Thiscall" => "Thiscall",
            _ => null
        };
        if (convention is null)
            return Reject("The .NET extension does not implement this unmanaged calling convention.");
        if ((runtime.functionPointerTransport == ManagedFunctionPointerTransport.NativeInteger
                || runtime.libraryImportConvention == ManagedImportConventionStrategy.RuntimeDefault)
            && convention != runtime.defaultCallingConvention)
            return Reject("The signature differs from the exact convention implemented by this execution profile.");
        if (provider.value is "dotnet.windows-x64" or "dotnet.system-v-amd64"
            && convention != "Cdecl")
            return Reject("The registered aggregate algorithm currently establishes only the exact C convention.");
        StableId? rejectedType = null;
        try
        {
            CallingAbi.AggregateClassifier? classifier = null;
            NativeCallValuePlan? result = Value(request.signature.returnType, allowVoid: true);
            ImmutableArray<NativeCallValuePlan> parameters = request.signature.parameterTypes.Select(id => Value(id, allowVoid: false)!).ToImmutableArray();
            return new(provider, NativeCallDisposition.Direct, "The complete physical signature is represented by this implemented .NET ABI.",
                new(request.signature, module.targetId, runtime.runtimeId, runtime.id, provider, module.pointerSize, module.byteOrder, result, parameters)
                { nativeTriple = target.triple, runtimeProfileDigest = profileDigest ?? ManagedRuntimeProfileIdentity.Capture(runtime) });

            NativeCallValuePlan? Value(
                StableId id,
                bool allowVoid
            ) {
                try { return ValueCore(id, allowVoid); }
                catch (NotSupportedException) { rejectedType ??= id; throw; }
            }

            NativeCallValuePlan? ValueCore(
                StableId id,
                bool allowVoid
            ) {
                InteropType type = Resolve(id);
                if (type.category == NativeTypeCategory.Primitive && type.primitiveKind == "Void")
                {
                    if (allowVoid) return null;
                    throw new NotSupportedException("Void cannot be used as a parameter carrier.");
                }
                if (type.category is NativeTypeCategory.Record or NativeTypeCategory.Array)
                {
                    if (provider.value == "dotnet.pointer-boundary")
                        throw new NotSupportedException("This execution profile requires aggregate values to use native pointer and output channels.");
                    return (classifier ??= new(module, provider.value)).ClassifyValue(id);
                }
                if (type.size is not > 0 || type.alignment is not > 0)
                    throw new NotSupportedException("A physical value has no complete native storage facts.");
                if (type.category == NativeTypeCategory.Enumeration && type.elementType is StableId integral)
                {
                    NativeCallValuePlan underlying = Value(integral, false)!;
                    if (type.size != underlying.size || type.alignment != underlying.alignment)
                        throw new NotSupportedException("The enum storage does not match its established integer carrier.");
                    return underlying with { nativeType = id };
                }
                if (type.category is NativeTypeCategory.Pointer or NativeTypeCategory.Function)
                {
                    if (type.size != module.pointerSize || type.alignment != module.pointerSize)
                        throw new NotSupportedException("A pointer carrier does not match the established native pointer storage.");
                    return new(id, type.size.Value, type.alignment.Value, ["Address"]);
                }
                if (type.category != NativeTypeCategory.Primitive || !(type.primitiveKind == "Float" && type.size == 4
                    || type.primitiveKind == "Double" && type.size == 8 || type.primitiveKind == "Bool" && type.size == 1
                    || type.isSigned is not null && type.size is 1 or 2 or 4 or 8))
                    throw new NotSupportedException($"The special native value '{type.nativeName}' has no implemented calling-ABI carrier; use an explicit pointer or semantic shim.");
                return new(id, type.size.Value, type.alignment.Value, [type.primitiveKind is "Float" or "Double" ? "Floating" : "Integer"]);
            }

            InteropType Resolve(StableId id)
            {
                HashSet<StableId>? seen = null;
                InteropType type = module.GetType(id);
                while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
                {
                    if (!(seen ??= []).Add(type.id) || type.elementType is not StableId next) throw new NotSupportedException("A call contains incomplete or cyclic aliases.");
                    type = module.GetType(next);
                }
                return type;
            }
        }
        catch (NotSupportedException failure)
        {
            return new(provider, NativeCallDisposition.RequiresBridge, failure.Message) { rejectionType = rejectedType };
        }

        CallAbiAssessment Reject(string reason) => new(provider, NativeCallDisposition.Unsupported, reason);
    }
}
