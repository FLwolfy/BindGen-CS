using BGCS.Contracts.Common;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;

namespace BGCS.Contracts.Artifacts;

/// <summary>Names the native consumption operation independently of filename extensions and target-ID spelling.</summary>
public enum NativeReferenceKind
{
    /// <summary>Copies the complete dynamic module to application output.</summary>
    DynamicCopy,
    /// <summary>Includes a Wasm object or archive as a browser native dependency.</summary>
    BrowserNativeFile,
    /// <summary>Includes an Apple mobile static archive or object using the native reference build contract.</summary>
    AppleStatic,
    /// <summary>Includes an object or archive in the NativeAOT application's native link.</summary>
    NativeAotLibrary
}

/// <summary>Preserves a verified native candidate's exact bytes, target facts and consumer build operation.</summary>
/// <param name="relativePath">The owned generation-output-relative native artifact path.</param>
/// <param name="digest">The verified complete file content identity.</param>
/// <param name="target">The resolved target whose opaque ID remains provenance.</param>
/// <param name="outputKind">The actual dynamic, archive or relocatable object form.</param>
/// <param name="kind">The required consumer build operation.</param>
/// <param name="runtimeIdentifier">The asset RID derived from known platform, CPU and ABI facts.</param>
/// <param name="importModuleName">The declared managed import identity, including the browser native-file registration identity.</param>
public sealed record NativeReferenceDescriptor(
    string relativePath,
    ContentDigest digest,
    NativeTargetDescriptor target,
    NativeOutputKind outputKind,
    NativeReferenceKind kind,
    string runtimeIdentifier,
    string importModuleName
);
