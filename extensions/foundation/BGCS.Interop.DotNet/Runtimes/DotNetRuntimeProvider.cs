using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using BGCS.Core;
using BGCS.Core.Calls;
using BGCS.Core.Native;
using BGCS.Core.Plugins;
using BGCS.Core.Ports;
using BGCS.Core.Requests;
using BGCS.Core.Targets;

namespace BGCS.Interop.DotNet;

/// <summary>Resolves supported CoreCLR desktop execution independently of the platform-neutral engine.</summary>
public sealed class DotNetRuntimeProvider : IManagedRuntimeProvider, ICacheFingerprintProvider
{
    /// <inheritdoc />
    public CacheFingerprintContribution CaptureFingerprint(
        CacheFingerprintContext context,
        CancellationToken cancellationToken
    )
        => ExtensionFingerprint.Capture(GetType(), cancellationToken);

    /// <inheritdoc />
    public bool TryResolve(
        ManagedRuntimeRequest request,
        [NotNullWhen(true)] out ManagedRuntimeProfile? profile
    ) {
        ArgumentNullException.ThrowIfNull(request);
        profile = null;
        bool implicitSelection = string.IsNullOrEmpty(request.runtimeId.value);
        if (!implicitSelection && request.runtimeId.value != "dotnet.coreclr") return false;
        NativeTargetDescriptor target = request.target;
        int pointerSize = target.architectureId switch { "x64" or "arm64" => 8, "x86" => 4, _ => 0 };
        string architecture = target.architectureId switch { "x64" => "x86_64-", "arm64" => "aarch64-", "x86" => "i686-", _ => string.Empty };
        bool knownDesktop = pointerSize > 0 && target.byteOrder == NativeByteOrder.LittleEndian
            && (target.triple.StartsWith(architecture, StringComparison.Ordinal)
                || target.platformId == "macos" && target.architectureId == "arm64" && target.triple.StartsWith("arm64-", StringComparison.Ordinal))
            && (target.platformId == "windows" && target.abiId == "msvc" && target.triple.Contains("windows-msvc", StringComparison.Ordinal)
                || target.platformId == "linux" && target.architectureId is "x64" or "arm64"
                    && (target.abiId == "gnu" && target.triple.Contains("linux-gnu", StringComparison.Ordinal)
                        || target.abiId == "musl" && target.triple.Contains("linux-musl", StringComparison.Ordinal))
                || target.platformId == "macos" && target.abiId == "darwin" && target.architectureId is "x64" or "arm64"
                    && (target.triple.Contains("darwin", StringComparison.Ordinal) || target.triple.Contains("macos", StringComparison.Ordinal)));
        if (!knownDesktop || target.capabilities is not { supportsDynamicModules: true })
        {
            if (implicitSelection) return false;
            throw new NotSupportedException("The CoreCLR desktop extension does not establish execution support for these exact native target facts.");
        }
        CallAbiId abi = new("dotnet.pointer-boundary");
        if (target.architectureId == "x64")
        {
            if (target.platformId == "windows") abi = new("dotnet.windows-x64");
            else if (target.platformId == "macos" || target.platformId == "linux" && target.abiId == "gnu") abi = new("dotnet.system-v-amd64");
        }
        ManagedRuntimeId runtimeId = new("dotnet.coreclr");
        profile = new(target.targetId.value + "/dotnet.coreclr/v1", runtimeId,
            NativeModuleMechanism.DynamicLibrary,
            [BindingImportMode.FunctionTable, BindingImportMode.DllImport, BindingImportMode.LibraryImport], false, true)
        {
            nativeTargetId = target.targetId.value,
            nativeTriple = target.triple,
            pointerSize = pointerSize,
            byteOrder = target.byteOrder,
            callAbi = abi,
            functionPointerTransport = ManagedFunctionPointerTransport.Typed,
            libraryImportConvention = ManagedImportConventionStrategy.Explicit
        };
        return true;
    }
}
