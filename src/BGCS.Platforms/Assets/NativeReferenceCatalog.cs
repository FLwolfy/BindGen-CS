using System;
using System.IO;
using System.Linq;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.Platforms.Capabilities;

namespace BGCS.Platforms.Assets;

/// <summary>Resolves consumer native build operations from verified output forms and managed runtime facts.</summary>
public static class NativeReferenceCatalog
{
    /// <summary>Creates a typed application build reference without inferring format from a path extension.</summary>
    /// <param name="target">The resolved native ABI and implemented target mechanisms.</param>
    /// <param name="runtime">The resolved application execution model.</param>
    /// <param name="outputKind">The independently verified native artifact form.</param>
    /// <param name="artifact">The owned relative path and exact verified file digest.</param>
    /// <param name="configuredNativeName">The configured managed library import identity.</param>
    /// <returns>The required native consumption operation; this is not invocation or package-install evidence.</returns>
    public static NativeReferenceDescriptor Create(
        NativeTargetDescriptor target,
        ManagedRuntimeProfile runtime,
        NativeOutputKind outputKind,
        ArtifactDescriptor artifact,
        string configuredNativeName
    ) {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(artifact);
        if (!Enum.IsDefined(outputKind) || !PlatformCapabilityCatalog.Get(target).outputKinds.Contains(outputKind))
            throw new NotSupportedException("The declared native consumption form is absent from the target's implemented mechanisms.");
        NativeReferenceKind kind;
        if (outputKind == NativeOutputKind.SharedLibrary)
        {
            if (runtime.kind == ManagedRuntimeKind.BrowserWasm || target.platformId is "ios" or "ios-simulator")
                throw new NotSupportedException("This runtime requires statically linked native artifacts.");
            kind = NativeReferenceKind.DynamicCopy;
        }
        else if (runtime.moduleMechanism != NativeModuleMechanism.StaticAdapter)
            throw new NotSupportedException("Native archive or object consumption requires the managed static adapter profile.");
        else if (runtime.kind == ManagedRuntimeKind.BrowserWasm && target.platformId == "emscripten" && target.architectureId == "wasm32" && target.abiId == "emscripten")
            kind = NativeReferenceKind.BrowserNativeFile;
        else if (runtime.kind == ManagedRuntimeKind.NativeAot && target.platformId is "ios" or "ios-simulator" && target.abiId == "darwin")
            kind = NativeReferenceKind.AppleStatic;
        else if (runtime.kind == ManagedRuntimeKind.NativeAot)
            kind = NativeReferenceKind.NativeAotLibrary;
        else throw new NotSupportedException("The managed runtime has no implemented static native consumption operation.");
        string importModuleName = NativeOutputLayout.ResolveImportModuleName(target, runtime, configuredNativeName, Path.GetFileName(artifact.relativePath));
        return new(artifact.relativePath, artifact.digest, target, outputKind, kind, RuntimeIdentifierMapper.Map(target), importModuleName);
    }
}
